using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TalentSuite.Server.Health;

[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly IReadOnlyCollection<IHealthCheckProbe> _probes;

    public HealthController(IEnumerable<IHealthCheckProbe> probes)
    {
        _probes = probes.ToArray();
    }

    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var checks = new List<HealthCheckResult>(_probes.Count);

        foreach (var probe in _probes)
        {
            checks.Add(await probe.CheckAsync(cancellationToken));
        }

        var response = new HealthResponse(checks.All(x => x.Success), checks);
        return response.Success ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
