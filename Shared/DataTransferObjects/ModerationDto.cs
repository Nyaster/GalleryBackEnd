using System.ComponentModel.DataAnnotations;
using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record ChangeModerationDto([property: EnumDataType(typeof(ModerationStatus))] ModerationStatus Status);
