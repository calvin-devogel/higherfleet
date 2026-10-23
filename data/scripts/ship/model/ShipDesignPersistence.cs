using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Higherfleet.Ships.Parts;

namespace Higherfleet.Ships.Model;

public static class ShipDesignPersistence
{
	private const int CurrentFormatVersion = 1;
	private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

	public const string SaveDirectory = "user://ship_designs";

	public static void EnsureSaveDirectory()
	{
		string absolutePath = ProjectSettings.GlobalizePath(SaveDirectory);
		Error error = DirAccess.MakeDirRecursiveAbsolute(absolutePath);
		if (error != Error.Ok && error != Error.AlreadyExists)
			throw new IOException($"Could not create ship save directory '{SaveDirectory}': {error}.");
	}

	public static void SaveToFile(string path, ShipDesign design)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(design);
		string json = Serialize(design);
		EnsureParentDirectory(path);
		string absolutePath = ProjectSettings.GlobalizePath(path);
		string temporaryPath = $"{absolutePath}.tmp";
		try
		{
			File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
			File.Move(temporaryPath, absolutePath, true);
		}
		catch (UnauthorizedAccessException exception)
		{
			throw new IOException($"Access denied while saving ship design '{path}'.", exception);
		}
	}

	public static ShipDesign LoadFromFile(
		string path,
		Func<string, ShipPartDefinition> resolvePartDefinition)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		try
		{
			return Deserialize(File.ReadAllText(ProjectSettings.GlobalizePath(path)), resolvePartDefinition);
		}
		catch (UnauthorizedAccessException exception)
		{
			throw new IOException($"Access denied while reading ship design '{path}'.", exception);
		}
	}

	public static string Serialize(ShipDesign design)
	{
		ArgumentNullException.ThrowIfNull(design);
		return JsonSerializer.Serialize(ToData(design), JsonOptions);
	}

	public static ShipDesign Deserialize(
		string json,
		Func<string, ShipPartDefinition> resolvePartDefinition)
	{
		ArgumentNullException.ThrowIfNull(json);
		ArgumentNullException.ThrowIfNull(resolvePartDefinition);

		ShipDesignFile data;
		try
		{
			data = JsonSerializer.Deserialize<ShipDesignFile>(json, JsonOptions)
				?? throw new InvalidDataException("The ship save file is empty.");
		}
		catch (JsonException exception)
		{
			throw new InvalidDataException("The ship save file contains invalid JSON.", exception);
		}

		if (data.FormatVersion != CurrentFormatVersion)
			throw new InvalidDataException(
				$"Unsupported ship save format version {data.FormatVersion}; expected {CurrentFormatVersion}.");
		if (string.IsNullOrWhiteSpace(data.DesignId))
			throw new InvalidDataException("The ship save file has no design ID.");
		if (data.RootPartInstanceId == null
			|| data.Parts == null || data.Connections == null || data.LogisticsRoutes == null
			|| data.LogisticsJunctions == null || data.LogisticsPassOvers == null
			|| data.LogisticsPassThroughs == null)
			throw new InvalidDataException("The ship save file is missing required design data.");

		ShipDesign design = new(data.DesignId);
		HashSet<string> partIds = new(StringComparer.Ordinal);
		foreach (ShipPartData partData in data.Parts)
		{
			if (partData == null || string.IsNullOrWhiteSpace(partData.InstanceId)
				|| string.IsNullOrWhiteSpace(partData.PartId)
				|| !partIds.Add(partData.InstanceId))
				throw new InvalidDataException("The ship save file contains an invalid or duplicate part.");

			ShipPartDefinition definition = resolvePartDefinition(partData.PartId)
				?? throw new InvalidDataException($"Unknown ship part definition '{partData.PartId}'.");
			RequireFinite(partData.Position, $"part '{partData.InstanceId}' position");
			if (!definition.AllowsRotation(partData.RotationDegrees))
				throw new InvalidDataException(
					$"Part '{partData.InstanceId}' has unsupported rotation {partData.RotationDegrees}.");

			design.AddPart(new ShipPartInstance(
				partData.InstanceId,
				definition,
				partData.Position.ToVector2(),
				partData.RotationDegrees));
		}

		if (!string.IsNullOrEmpty(data.RootPartInstanceId))
		{
			if (!partIds.Contains(data.RootPartInstanceId))
				throw new InvalidDataException(
					$"The ship save root '{data.RootPartInstanceId}' does not refer to a saved part.");
			design.SetRootPart(data.RootPartInstanceId);
		}

		foreach (PhysicalConnectionData connection in data.Connections)
		{
			RequirePortReference(connection?.First, partIds, design, "mount");
			RequirePortReference(connection.Second, partIds, design, "mount");
			design.AddConnection(new PhysicalConnection(
				new PhysicalPortReference(connection.First.PartInstanceId, connection.First.PortId),
				new PhysicalPortReference(connection.Second.PartInstanceId, connection.Second.PortId)));
		}

		HashSet<string> routeIds = new(StringComparer.Ordinal);
		foreach (LogisticsRouteData route in data.LogisticsRoutes)
		{
			if (route == null || string.IsNullOrWhiteSpace(route.RouteId)
				|| !routeIds.Add(route.RouteId))
				throw new InvalidDataException("The ship save file contains an invalid logistics route.");
			RequireEnumValue(route.System, $"route '{route.RouteId}' system");
			LogisticsEndpoint start = FromData(route.Start, partIds, design, $"route '{route.RouteId}' start");
			LogisticsEndpoint end = FromData(route.End, partIds, design, $"route '{route.RouteId}' end");
			if (route.Waypoints == null)
				throw new InvalidDataException($"Route '{route.RouteId}' has no waypoint list.");
			foreach (VectorData waypoint in route.Waypoints)
				RequireFinite(waypoint, $"route '{route.RouteId}' waypoint");

			design.AddLogisticsRoute(new LogisticsRoute(
				route.RouteId,
				route.System,
				start,
				end,
				route.Waypoints.Select(point => point.ToVector2())));
		}

		HashSet<string> junctionIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> junctionPositions = new();
		foreach (LogisticsJunctionData marker in data.LogisticsJunctions)
		{
			RequireMarker(marker?.JunctionId, marker?.Position, marker?.System, "junction");
			Vector2 position = marker.Position.ToVector2();
			if (!junctionIds.Add(marker.JunctionId)
				|| !junctionPositions.Add((marker.System, position)))
				throw new InvalidDataException($"Duplicate logistics junction '{marker.JunctionId}'.");
			design.AddLogisticsJunction(new LogisticsJunction(
				marker.JunctionId, marker.System, position));
		}

		HashSet<string> passOverIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> passOverPositions = new();
		foreach (LogisticsPassOverData marker in data.LogisticsPassOvers)
		{
			RequireMarker(marker?.MarkerId, marker?.Position, marker?.System, "pass-over");
			Vector2 position = marker.Position.ToVector2();
			if (!passOverIds.Add(marker.MarkerId)
				|| !passOverPositions.Add((marker.System, position)))
				throw new InvalidDataException($"Duplicate logistics pass-over '{marker.MarkerId}'.");
			design.AddLogisticsPassOver(new LogisticsPassOver(
				marker.MarkerId, marker.System, position));
		}

		HashSet<string> passThroughIds = new(StringComparer.Ordinal);
		HashSet<(LogisticsSystem System, Vector2 Position)> passThroughPositions = new();
		foreach (LogisticsPassThroughData marker in data.LogisticsPassThroughs)
		{
			RequireMarker(marker?.MarkerId, marker?.Position, marker?.System, "pass-through");
			Vector2 position = marker.Position.ToVector2();
			if (!passThroughIds.Add(marker.MarkerId)
				|| !passThroughPositions.Add((marker.System, position)))
				throw new InvalidDataException($"Duplicate logistics pass-through '{marker.MarkerId}'.");
			design.AddLogisticsPassThrough(new LogisticsPassThrough(
				marker.MarkerId, marker.System, position));
		}

		return design;
	}

	private static JsonSerializerOptions CreateJsonOptions()
	{
		JsonSerializerOptions options = new() { WriteIndented = true };
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}

	private static ShipDesignFile ToData(ShipDesign design)
	{
		return new ShipDesignFile
		{
			FormatVersion = CurrentFormatVersion,
			DesignId = design.DesignId,
			RootPartInstanceId = design.RootPartInstanceId,
			Parts = design.Parts.Select(part => new ShipPartData
			{
				InstanceId = part.InstanceId,
				PartId = part.Definition.PartId,
				Position = VectorData.FromVector2(part.Position),
				RotationDegrees = part.RotationDegrees
			}).ToList(),
			Connections = design.Connections.Select(connection => new PhysicalConnectionData
			{
				First = new PhysicalPortData
				{
					PartInstanceId = connection.First.PartInstanceId,
					PortId = connection.First.PortId
				},
				Second = new PhysicalPortData
				{
					PartInstanceId = connection.Second.PartInstanceId,
					PortId = connection.Second.PortId
				}
			}).ToList(),
			LogisticsRoutes = design.LogisticsRoutes.Select(route => new LogisticsRouteData
			{
				RouteId = route.RouteId,
				System = route.System,
				Start = ToData(route.Start),
				End = ToData(route.End),
				Waypoints = route.Waypoints.Select(VectorData.FromVector2).ToList()
			}).ToList(),
			LogisticsJunctions = design.LogisticsJunctions.Select(marker => new LogisticsJunctionData
			{
				JunctionId = marker.JunctionId,
				System = marker.System,
				Position = VectorData.FromVector2(marker.Position)
			}).ToList(),
			LogisticsPassOvers = design.LogisticsPassOvers.Select(marker => new LogisticsPassOverData
			{
				MarkerId = marker.MarkerId,
				System = marker.System,
				Position = VectorData.FromVector2(marker.Position)
			}).ToList(),
			LogisticsPassThroughs = design.LogisticsPassThroughs.Select(marker => new LogisticsPassThroughData
			{
				MarkerId = marker.MarkerId,
				System = marker.System,
				Position = VectorData.FromVector2(marker.Position)
			}).ToList()
		};
	}

	private static LogisticsEndpointData ToData(LogisticsEndpoint endpoint)
	{
		return new LogisticsEndpointData
		{
			Position = VectorData.FromVector2(endpoint.Position),
			Port = endpoint.Port.HasValue
				? new LogisticsPortData
				{
					PartInstanceId = endpoint.Port.Value.PartInstanceId,
					PortId = endpoint.Port.Value.PortId
				}
				: null
		};
	}

	private static LogisticsEndpoint FromData(
		LogisticsEndpointData endpoint,
		HashSet<string> partIds,
		ShipDesign design,
		string description)
	{
		if (endpoint == null)
			throw new InvalidDataException($"The ship save file is missing {description}.");
		RequireFinite(endpoint.Position, description);
		if (endpoint.Port == null)
			return new LogisticsEndpoint(endpoint.Position.ToVector2());

		ShipPartInstance part = FindPart(endpoint.Port.PartInstanceId, partIds, design, description);
		if (part.Definition.LogisticsPorts.All(port => port.PortId != endpoint.Port.PortId))
			throw new InvalidDataException($"{description} refers to unknown logistics port '{endpoint.Port.PortId}'.");
		return new LogisticsEndpoint(
			endpoint.Position.ToVector2(),
			new LogisticsPortReference(endpoint.Port.PartInstanceId, endpoint.Port.PortId));
	}

	private static void RequirePortReference(
		PhysicalPortData reference,
		HashSet<string> partIds,
		ShipDesign design,
		string portType)
	{
		if (reference == null)
			throw new InvalidDataException($"The ship save file is missing a {portType} port reference.");
		ShipPartInstance part = FindPart(reference.PartInstanceId, partIds, design, portType);
		if (part.Definition.FindMountPort(reference.PortId) == null)
			throw new InvalidDataException($"Unknown {portType} port '{reference.PortId}' on '{reference.PartInstanceId}'.");
	}

	private static ShipPartInstance FindPart(
		string instanceId,
		HashSet<string> partIds,
		ShipDesign design,
		string description)
	{
		if (string.IsNullOrWhiteSpace(instanceId) || !partIds.Contains(instanceId))
			throw new InvalidDataException($"{description} refers to missing part '{instanceId}'.");
		return design.Parts.First(part => part.InstanceId == instanceId);
	}

	private static void RequireMarker(
		string markerId,
		VectorData position,
		LogisticsSystem? system,
		string markerType)
	{
		if (string.IsNullOrWhiteSpace(markerId) || !system.HasValue)
			throw new InvalidDataException($"The ship save file contains an invalid {markerType} marker.");
		RequireEnumValue(system.Value, $"{markerType} '{markerId}' system");
		RequireFinite(position, $"{markerType} '{markerId}' position");
	}

	private static void RequireEnumValue<TEnum>(TEnum value, string description)
		where TEnum : struct, Enum
	{
		if (!Enum.IsDefined(value))
			throw new InvalidDataException($"The ship save file contains an unknown {description}.");
	}

	private static void RequireFinite(VectorData vector, string description)
	{
		if (vector == null || !float.IsFinite(vector.X) || !float.IsFinite(vector.Y))
			throw new InvalidDataException($"The ship save file contains an invalid {description}.");
	}

	private static void EnsureParentDirectory(string path)
	{
		string absolutePath = ProjectSettings.GlobalizePath(path);
		string parentDirectory = Path.GetDirectoryName(absolutePath);
		if (string.IsNullOrEmpty(parentDirectory))
			throw new IOException($"Ship save path '{path}' has no parent directory.");

		Error error = DirAccess.MakeDirRecursiveAbsolute(parentDirectory);
		if (error != Error.Ok && error != Error.AlreadyExists)
			throw new IOException($"Could not create directory for ship save '{path}': {error}.");
	}
}

internal sealed class ShipDesignFile
{
	public int FormatVersion { get; set; }
	public string DesignId { get; set; } = string.Empty;
	public string RootPartInstanceId { get; set; }
	public List<ShipPartData> Parts { get; set; }
	public List<PhysicalConnectionData> Connections { get; set; }
	public List<LogisticsRouteData> LogisticsRoutes { get; set; }
	public List<LogisticsJunctionData> LogisticsJunctions { get; set; }
	public List<LogisticsPassOverData> LogisticsPassOvers { get; set; }
	public List<LogisticsPassThroughData> LogisticsPassThroughs { get; set; }
}

internal sealed class ShipPartData
{
	public string InstanceId { get; set; } = string.Empty;
	public string PartId { get; set; } = string.Empty;
	public VectorData Position { get; set; }
	public int RotationDegrees { get; set; }
}

internal sealed class PhysicalConnectionData
{
	public PhysicalPortData First { get; set; }
	public PhysicalPortData Second { get; set; }
}

internal sealed class PhysicalPortData
{
	public string PartInstanceId { get; set; } = string.Empty;
	public string PortId { get; set; } = string.Empty;
}

internal sealed class LogisticsRouteData
{
	public string RouteId { get; set; } = string.Empty;
	public LogisticsSystem System { get; set; }
	public LogisticsEndpointData Start { get; set; }
	public LogisticsEndpointData End { get; set; }
	public List<VectorData> Waypoints { get; set; }
}

internal sealed class LogisticsEndpointData
{
	public VectorData Position { get; set; }
	public LogisticsPortData Port { get; set; }
}

internal sealed class LogisticsPortData
{
	public string PartInstanceId { get; set; } = string.Empty;
	public string PortId { get; set; } = string.Empty;
}

internal sealed class LogisticsJunctionData
{
	public string JunctionId { get; set; } = string.Empty;
	public LogisticsSystem System { get; set; }
	public VectorData Position { get; set; }
}

internal sealed class LogisticsPassOverData
{
	public string MarkerId { get; set; } = string.Empty;
	public LogisticsSystem System { get; set; }
	public VectorData Position { get; set; }
}

internal sealed class LogisticsPassThroughData
{
	public string MarkerId { get; set; } = string.Empty;
	public LogisticsSystem System { get; set; }
	public VectorData Position { get; set; }
}

internal sealed class VectorData
{
	public float X { get; set; }
	public float Y { get; set; }

	public static VectorData FromVector2(Vector2 vector) => new() { X = vector.X, Y = vector.Y };
	public Vector2 ToVector2() => new(X, Y);
}
