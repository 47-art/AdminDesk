using AdminDesk.Api.Auth;
using AdminDesk.Api.Filters;
using AdminDesk.Application.Documents;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Exceptions;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

// Maps the document calls and wraps the envelope. Every rule lives in the document service.
[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Requests)]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documents;
    private readonly ClaimsActorContextFactory _actors;

    public DocumentsController(IDocumentService documents, ClaimsActorContextFactory actors)
    {
        _documents = documents;
        _actors = actors;
    }

    [HttpGet("{requestId:long}/documents")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DocumentDto>>>> List(long requestId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<DocumentDto>>.Ok(await _documents.ListAsync(_actors.Create(User), requestId, ct)));

    [HttpPost("{requestId:long}/documents")]
    [TypeFilter(typeof(DocumentSizeLimitFilter))]
    public async Task<ActionResult<ApiResponse<DocumentDto>>> Upload(
        long requestId, IFormFile? file, [FromForm] string? stepKey, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ValidationException("file", "Choose a file to upload.");
        }

        await using var content = file.OpenReadStream();
        var upload = new DocumentUpload(file.FileName, file.ContentType, file.Length, content);
        var created = await _documents.UploadAsync(_actors.Create(User), requestId, upload, stepKey, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<DocumentDto>.Ok(created));
    }

    [HttpGet("{requestId:long}/documents/{documentId:long}/download")]
    public async Task<IActionResult> Download(long requestId, long documentId, CancellationToken ct)
    {
        var download = await _documents.DownloadAsync(_actors.Create(User), requestId, documentId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // The result owns the stream and disposes it once the file is sent; the name is sent as an attachment.
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpDelete("{requestId:long}/documents/{documentId:long}")]
    public async Task<ActionResult<ApiResponse>> Remove(long requestId, long documentId, CancellationToken ct)
    {
        await _documents.RemoveAsync(_actors.Create(User), requestId, documentId, ct);
        return Ok(ApiResponse.Ok());
    }
}
