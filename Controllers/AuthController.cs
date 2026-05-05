using Microsoft.AspNetCore.Mvc;
using MaghrebButusAPI.Models;
using MaghrebButusAPI.Services;

namespace MaghrebButusAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly FirebaseAuthService _auth;

        public AuthController(FirebaseAuthService auth)
        {
            _auth = auth;
        }

        /// POST /api/auth/verifier-licence
        /// Le plugin envoie son token Firebase + machineId
        /// Retourne un token de session signé valable 8h
        [HttpPost("verifier-licence")]
        public async Task<IActionResult> VerifierLicence([FromBody] LicenceCheckRequest request)
        {
            if (string.IsNullOrEmpty(request.IdToken))
                return BadRequest(new LicenceCheckResponse { Autorise = false, Message = "Token manquant" });

            if (string.IsNullOrEmpty(request.MachineId))
                return BadRequest(new LicenceCheckResponse { Autorise = false, Message = "MachineId manquant" });

            var result = await _auth.VerifierLicenceAsync(request);

            if (!result.Autorise)
                return Unauthorized(result);

            return Ok(result);
        }
    }
}
