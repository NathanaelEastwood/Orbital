using System;
using System.Numerics;

namespace Shared;

public class ShipStateDto
{
    public Guid Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
}