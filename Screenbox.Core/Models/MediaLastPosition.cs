using ProtoBuf;
using System;

namespace Screenbox.Core.Models
{
    [ProtoContract]
    internal record MediaLastPosition(string Location, TimeSpan Position, DateTime LastWatchedUtc)
    {
        [ProtoMember(1)] public string Location { get; set; } = Location;
        [ProtoMember(2)] public TimeSpan Position { get; set; } = Position;
        [ProtoMember(3)] public DateTime LastWatchedUtc { get; set; } = LastWatchedUtc;

        public MediaLastPosition(string location, TimeSpan position) : this(location, position, DateTime.UtcNow)
        {
        }

        public MediaLastPosition() : this(string.Empty, TimeSpan.Zero)
        {
        }
    }
}
