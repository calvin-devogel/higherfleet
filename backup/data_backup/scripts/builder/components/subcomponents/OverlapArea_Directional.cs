using System.Collections.Generic;
using Godot;

namespace Builder.Components.External
{
    public class OverlapArea_Directional
    {
        public Area2D North { get; set; }
        public Area2D South { get; set; }
        public Area2D East { get; set; }
        public Area2D West { get; set; }

        public Area2D this[Face face]
        {
            get => face switch
            {
                Face.North => North,
                Face.South => South,
                Face.East => East,
                Face.West => West,
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
                }
            }
        }

        public IEnumerable<Area2D> All => new[] { North, South, East, West };

        public bool TryGet(Face face, out Area2D area)
        {
            area = this[face];
            return area != null;
        }
    }
}