using System.Text.Json.Serialization;

namespace HappyRide.Api.Features.Dev;

public sealed record DevUserDto(
    Guid Id,
    string Name,
    string Role,
    string City,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Plate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Lat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Lng = null);
