using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Application.Features.Images.Queries;

public record GetImageContentQuery(int Id, bool AsJpeg) : IRequest<IActionResult>;