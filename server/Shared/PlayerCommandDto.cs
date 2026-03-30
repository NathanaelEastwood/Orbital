using System;

namespace Shared;

public class PlayerCommandDto
{
    public Guid ShipId { get; set; }
    public PlayerInputDto Input { get; set; } = new();
}

