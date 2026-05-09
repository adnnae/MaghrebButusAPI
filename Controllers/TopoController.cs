using Microsoft.AspNetCore.Mvc;
using MaghrebButusAPI.Models;
using MaghrebButusAPI.Services;

namespace MaghrebButusAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TopoController : ControllerBase
    {
        private readonly TopoService _topo;
        private readonly FirebaseAuthService _firebase;

        public TopoController(TopoService topo, FirebaseAuthService firebase)
        {
            _topo = topo;
            _firebase = firebase;
        }

        /// POST /api/topo/convert-coords
        [HttpPost("convert-coords")]
        public async Task<IActionResult> ConvertCoordinates([FromBody] CoordConvertRequest request)
        {
            if (request.Points == null || request.Points.Count == 0)
                return BadRequest(new CoordConvertResponse { Success = false, Message = "Aucun point fourni" });

            if (request.Points.Count > 500)
                return BadRequest(new CoordConvertResponse { Success = false, Message = "Maximum 500 points par requête" });

            var authResult = VerifySession("topo");
            if (authResult != null) return authResult;

            try
            {
                var result = await _topo.ConvertCoordinatesAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new CoordConvertResponse { Success = false, Message = $"Erreur conversion: {ex.Message}" });
            }
        }

        /// POST /api/topo/elevations
        [HttpPost("elevations")]
        public async Task<IActionResult> GetElevations([FromBody] ElevationRequest request)
        {
            if (request.Points == null || request.Points.Count == 0)
                return BadRequest(new ElevationResponse { Success = false, Message = "Aucun point fourni" });

            if (request.Points.Count > 1000)
                return BadRequest(new ElevationResponse { Success = false, Message = "Maximum 1000 points par requête" });

            var authResult = VerifySession("topo");
            if (authResult != null) return authResult;

            try
            {
                var result = await _topo.GetElevationsAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ElevationResponse { Success = false, Message = ex.Message });
            }
        }

        /// POST /api/topo/tile-grid
        [HttpPost("tile-grid")]
        public async Task<IActionResult> ComputeTileGrid([FromBody] TileGridRequest request)
        {
            if (request.Epsg == 0)
                return BadRequest(new TileGridResponse { Success = false, Message = "EPSG requis" });

            var authResult = VerifySession("topo");
            if (authResult != null) return authResult;

            try
            {
                var result = await _topo.ComputeTileGridAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new TileGridResponse { Success = false, Message = ex.Message });
            }
        }

        /// POST /api/topo/contours
        [HttpPost("contours")]
        public async Task<IActionResult> GenerateContours([FromBody] ContourRequest request)
        {
            if (request.Epsg == 0)
                return BadRequest(new ContourResponse { Success = false, Message = "EPSG requis" });

            if (request.Equidistance <= 0)
                return BadRequest(new ContourResponse { Success = false, Message = "Équidistance invalide" });

            var authResult = VerifySession("topo");
            if (authResult != null) return authResult;

            try
            {
                var result = await _topo.GenerateContoursAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ContourResponse { Success = false, Message = ex.Message });
            }
        }

        /// GET /api/topo/ping
        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new { status = "TopoMaghreb API v1.0", time = DateTime.UtcNow });

        // ── Vérification session + fonction ───────────────────────────────────
        private IActionResult? VerifySession(string fonction)
        {
            string sessionToken = Request.Headers["X-Session-Token"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(sessionToken))
                return Unauthorized(new { success = false, message = "Token de session manquant. Reconnectez-vous." });

            var principal = _firebase.VerifierSessionToken(sessionToken);
            if (principal == null)
                return Unauthorized(new { success = false, message = "Session expirée. Relancez l'application." });

            // Accept either 'topo' or 'hydraproject' for topo endpoints
            if (!_firebase.TokenAutoriseFonction(principal, fonction)
                && !_firebase.TokenAutoriseFonction(principal, "hydraproject"))
                return Unauthorized(new { success = false, message = $"Fonction '{fonction}' non incluse dans votre licence." });

            return null;
        }
    }
}
