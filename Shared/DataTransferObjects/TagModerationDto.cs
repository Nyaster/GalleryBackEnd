using System.ComponentModel.DataAnnotations;
using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record TagModerationDto([param: EnumDataType(typeof(TagModerationStatus))] TagModerationStatus Status);
