using System.Collections.Generic;

namespace Builder.Components.External
{
    public enum Face
    {
        North,
        South,
        East,
        West,
        NorthWest,
        NorthEast,
        EastNorth,
        EastSouth,
        SouthEast,
        SouthWest,
        WestNorth,
        WestSouth
    }

    public class SnapPoint_Directional
    {
        public SnapPoint_External North { get; set; }
        public SnapPoint_External South { get; set; }
        public SnapPoint_External East { get; set; }
        public SnapPoint_External West { get; set; }
        public SnapPoint_External NorthWest { get; set; }
        public SnapPoint_External NorthEast { get; set; }
        public SnapPoint_External EastNorth { get; set; }
        public SnapPoint_External EastSouth { get; set; }
        public SnapPoint_External SouthEast { get; set; }
        public SnapPoint_External SouthWest { get; set; }
        public SnapPoint_External WestNorth { get; set; }
        public SnapPoint_External WestSouth { get; set; }

        public SnapPoint_External this[Face face]
        {
            get => face switch
            {
                Face.North => North,
                Face.South => South,
                Face.East => East,
                Face.West => West,
                Face.NorthWest => NorthWest,
                Face.NorthEast => NorthEast,
                Face.EastNorth => EastNorth,
                Face.EastSouth => EastSouth,
                Face.SouthEast => SouthEast,
                Face.SouthWest => SouthWest,
                Face.WestNorth => WestNorth,
                Face.WestSouth => WestSouth,
                _ => null,
            };
            set
            {
                switch (face)
                {
                    case Face.North: North = value; break;
                    case Face.South: South = value; break;
                    case Face.East: East = value; break;
                    case Face.West: West = value; break;
                    case Face.NorthWest: NorthWest = value; break;
                    case Face.NorthEast: NorthEast = value; break;
                    case Face.EastNorth: EastNorth = value; break;
                    case Face.EastSouth: EastSouth = value; break;
                    case Face.SouthEast: SouthEast = value; break;
                    case Face.SouthWest: SouthWest = value; break;
                    case Face.WestNorth: WestNorth = value; break;
                    case Face.WestSouth: WestSouth = value; break;
                }
            }
        }

        public IEnumerable<SnapPoint_External> All => new[] { North, South, East, West, NorthWest, NorthEast, EastNorth, EastSouth, SouthEast, SouthWest, WestNorth, WestSouth };

        public bool TryGet(Face face, out SnapPoint_External snapPoint)
        {
            snapPoint = this[face];
            return snapPoint != null;
        }

        public Face? Opposite(Face face) =>
            face switch
            {
                Face.North => Face.South,
                Face.South => Face.North,
                Face.East => Face.West,
                Face.West => Face.East,
                Face.NorthWest => Face.SouthEast,
                Face.NorthEast => Face.SouthWest,
                Face.EastNorth => Face.WestSouth,
                Face.EastSouth => Face.WestNorth,
                Face.SouthEast => Face.NorthWest,
                Face.SouthWest => Face.NorthEast,
                Face.WestNorth => Face.EastSouth,
                Face.WestSouth => Face.EastNorth,
                _ => null,
            };

        public Face? Opposite_Vertical_Small(Face face) =>
            face switch
            {
                Face.NorthWest => Face.SouthWest,
                Face.NorthEast => Face.SouthEast,
                Face.SouthEast => Face.NorthEast,
                Face.SouthWest => Face.NorthWest,
                _ => null,
            };
        
        public Face? Opposite_Horizontal_Small(Face face) =>
            face switch
            {
                Face.EastNorth => Face.WestNorth,
                Face.EastSouth => Face.WestSouth,
                Face.WestNorth => Face.EastNorth,
                Face.WestSouth => Face.EastSouth,
                _ => null,
            };
    }
}