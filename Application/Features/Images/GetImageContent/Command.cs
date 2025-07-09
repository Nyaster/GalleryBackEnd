using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Application.Features.Images.GetImageContent;

public record Command(int Id, bool AsJpeg) : IRequest<IActionResult>;