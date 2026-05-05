using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MaghrebButusAPI.Models;
using MaghrebButusAPI.Services;

namespace MaghrebButusAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalageController : ControllerBase
    {
        private readonly CalageService _calage;
        private readonly FirebaseAuthService _firebase;

        public CalageController(CalageService calage, FirebaseAuthService firebase)
        {
            _calage = calage;
            _firebase = firebase;
        }

        /// POST /api/calage/automatique
        [HttpPost("automatique")]
        public async Task<IActionResult> CalageAutomatique([FromBody] CalageRequest request)
        {
            if (request.Collecteurs == null || request.Collecteurs.Count == 0)
                return BadRequest(new CalageResponse { Success = false, Message = "Aucun collecteur fourni" });

            // Vérifier le token de session
            string sessionToken = Request.Headers["X-Session-Token"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(sessionToken))
                return Unauthorized(new CalageResponse { Success = false, Message = "Token de session manquant. Reconnectez-vous." });

            var principal = _firebase.VerifierSessionToken(sessionToken);
            if (principal == null)
                return Unauthorized(new CalageResponse { Success = false, Message = "Session expirée. Relancez AutoCAD." });

            if (!_firebase.TokenAutoriseFonction(principal, "calage"))
                return Unauthorized(new CalageResponse { Success = false, Message = "Fonction Calage non incluse dans votre licence." });

            try
            {
                var result = _calage.CalculerCalage(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new CalageResponse { Success = false, Message = ex.Message });
            }
        }
    }
}
