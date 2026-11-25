using System.ComponentModel.DataAnnotations;
using Entities.Models;

namespace Shared.DataTransferObjects;

public sealed record ChangeModerationDto(
    [param: EnumDataType(typeof(ModerationStatus))] ModerationStatus? Status = null,
    [param: EnumDataType(typeof(AiUsageClassification))] AiUsageClassification? AiUsage = null,
    [param: EnumDataType(typeof(ModerationStatus))] ModerationStatus? ExpectedStatus = null);
