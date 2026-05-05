using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using MaghrebButusAPI.Models;
using MaghrebButusAPI.Services;

namespace MaghrebButusAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly FirebaseAuthService _auth;

        // Clé admin secrète — à mettre en variable d'environnement en production
        private const string ADMIN_KEY = "MaghrebButus_AdminKey_2025!";

        public AdminController(FirebaseAuthService auth)
        {
            _auth = auth;
        }

        /// POST /api/admin/creer-utilisateur
        /// Crée un compte Firebase Auth pour un nouveau client
        [HttpPost("creer-utilisateur")]
        public async Task<IActionResult> CreerUtilisateur([FromBody] CreerUtilisateurRequest req)
        {
            // Vérifier la clé admin
            if (req.AdminKey != ADMIN_KEY)
                return Unauthorized(new { success = false, message = "Clé admin invalide" });

            try
            {
                // Créer l'utilisateur dans Firebase Auth
                var userArgs = new UserRecordArgs
                {
                    Email = req.Email,
                    Password = req.MotDePasse,
                    DisplayName = req.Utilisateur,
                    EmailVerified = true,
                    Disabled = false
                };

                UserRecord user;
                try
                {
                    user = await FirebaseAuth.DefaultInstance.CreateUserAsync(userArgs);
                }
                catch (FirebaseAuthException ex) when (ex.Message.Contains("EMAIL_EXISTS"))
                {
                    // L'utilisateur existe déjà — on met juste à jour le mot de passe
                    user = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(req.Email);
                    await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
                    {
                        Uid = user.Uid,
                        Password = req.MotDePasse,
                        DisplayName = req.Utilisateur
                    });
                }

                return Ok(new { success = true, uid = user.Uid, message = $"Utilisateur {req.Email} créé/mis à jour" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class CreerUtilisateurRequest
    {
        public string AdminKey { get; set; } = "";
        public string Email { get; set; } = "";
        public string MotDePasse { get; set; } = "";
        public string Utilisateur { get; set; } = "";
    }
}
