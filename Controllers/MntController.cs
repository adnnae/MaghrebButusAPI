using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MaghrebButusAPI.Models;
using MaghrebButusAPI.Services;

namespace MaghrebButusAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MntController : ControllerBase
    {
        private readonly MntService _mnt;
        private readonly FirebaseAuthService _firebase;

        public MntController(MntService mnt, FirebaseAuthService firebase)
        {
            _mnt = mnt;
            _firebase = firebase;
        }

        /// POST /api/mnt/calculer
        /// Body: { "machineId": "...", "points": [{x,y,z},...] }
        [HttpPost("calculer")]
        [AllowAnonymous]
        public async Task<IActionResult> Calculer([FromBody] MntRequest request)
        {
            // Validation basique
            if (request.Points == null || request.Points.Count < 3)
                return BadRequest(new MntResponse { Success = false, Message = "Minimum 3 points requis" });

            if (string.IsNullOrEmpty(request.MachineId))
                return BadRequest(new MntResponse { Success = false, Message = "MachineId manquant" });

            if (request.Points.Count > 50000)
                return BadRequest(new MntResponse { Success = false, Message = "Maximum 50 000 points" });

            // Vérifier le token de session
            string sessionToken = Request.Headers["X-Session-Token"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(sessionToken))
                return Unauthorized(new MntResponse { Success = false, Message = "Token de session manquant. Reconnectez-vous." });

            var principal = _firebase.VerifierSessionToken(sessionToken);
            if (principal == null)
                return Unauthorized(new MntResponse { Success = false, Message = "Session expirée. Relancez AutoCAD." });

            if (!_firebase.TokenAutoriseFonction(principal, "mnt"))
                return Unauthorized(new MntResponse { Success = false, Message = "Fonction MNT non incluse dans votre licence." });

            try
            {
                var triangles = _mnt.Triangulate(request.Points);

                return Ok(new MntResponse
                {
                    Success = true,
                    Message = "MNT calculé avec succès",
                    Triangles = triangles,
                    PointCount = request.Points.Count,
                    TriangleCount = triangles.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new MntResponse { Success = false, Message = ex.Message });
            }
        }

        /// GET /api/mnt/ping — test de connectivité
        [HttpGet("ping")]
        [AllowAnonymous]
        public IActionResult Ping() => Ok(new { status = "Maghreb Butus API v1.0", time = DateTime.UtcNow });
    }
}
