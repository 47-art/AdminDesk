using System.Globalization;
using AdminDesk.Application.Auth;
using AdminDesk.Application.Masters;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminDesk.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.Authenticated)]
[Route(ApiRoutes.Me)]
public class MeController : ControllerBase
{
    private readonly IEmployeeRepository _employees;

    public MeController(IEmployeeRepository employees)
    {
        _employees = employees;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<MeDto>>> Get(CancellationToken ct)
    {
        var id = User.FindFirst(Claims.Subject)?.Value ?? string.Empty;
        var name = User.FindFirst(Claims.Name)?.Value ?? string.Empty;
        var email = User.FindFirst(Claims.Email)?.Value ?? string.Empty;
        var roles = User.FindAll(Claims.Role).Select(c => c.Value).ToList();

        long? employeeId = null;
        if (long.TryParse(User.FindFirst(Claims.EmployeeId)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            employeeId = parsed;
        }

        string? code = null, department = null, designation = null;
        if (employeeId is { } eid)
        {
            var employee = await _employees.GetByIdAsync(eid, ct);
            if (employee is not null)
            {
                code = employee.Code;
                department = employee.DepartmentName;
                designation = employee.Designation;
            }
        }

        return Ok(ApiResponse<MeDto>.Ok(new MeDto(id, name, email, employeeId, code, department, designation, roles)));
    }
}
