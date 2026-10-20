using System.Text.Json.Serialization;

namespace GrabBike.Api.Features.Dev;

public sealed record DevUserDto(
    Guid Id,
    string Name,
    string Role,
    string City,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Plate);
