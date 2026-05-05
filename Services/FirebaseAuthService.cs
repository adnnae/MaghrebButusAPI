using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using MaghrebButusAPI.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;

namespace MaghrebButusAPI.Services
{
    public class FirebaseAuthService
    {
        private readonly HttpClient _http;
        private readonly string _dbUrl = "https://butus-52a53-default-rtdb.firebaseio.com";

        // Clé secrète lue depuis la configuration (variable d'environnement en production)
        private readonly string _sessionSecret;

        public FirebaseAuthService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _sessionSecret = config["SessionSecret"]
                ?? "MaghrebButus_SecretKey_2025_!@#$%^&*()_SuperSecure_Dev";
        }

        // ── 1. Vérifier le token Firebase ID ─────────────────────────────
        public async Task<FirebaseToken?> VerifyFirebaseTokenAsync(string idToken)
        {
            try
            {
                return await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
            }
            catch
            {
                return null;
            }
        }

        // ── 2. Lire la licence depuis Firebase Realtime Database ──────────
        public async Task<LicenceInfo?> GetLicenceByEmailAsync(string email)
        {
            try
            {
                string emailKey = email.Replace(".", "_").Replace("@", "_at_");
                string accessToken = await GetServiceAccountTokenAsync();
                var url = $"{_dbUrl}/licences/{emailKey}.json?access_token={accessToken}";

                var response = await _http.GetAsync(url);
                string json = await response.Content.ReadAsStringAsync();

                // Log pour debug
                Console.WriteLine($"[LICENCE] Email: {email} → Clé: {emailKey}");
                Console.WriteLine($"[LICENCE] URL: {url}");
                Console.WriteLine($"[LICENCE] Status: {response.StatusCode}");
                Console.WriteLine($"[LICENCE] JSON: {json}");

                if (!response.IsSuccessStatusCode) return null;
                if (json == "null" || string.IsNullOrWhiteSpace(json)) return null;

                return System.Text.Json.JsonSerializer.Deserialize<LicenceInfo>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LICENCE] Exception: {ex.Message}");
                return null;
            }
        }

        // Obtenir un token d'accès depuis le compte de service Firebase
        private async Task<string> GetServiceAccountTokenAsync()
        {
            GoogleCredential credential;

            // En production (Render) : lire depuis variable d'environnement
            var json = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT");
            if (!string.IsNullOrEmpty(json))
            {
                credential = GoogleCredential.FromJson(json)
                    .CreateScoped("https://www.googleapis.com/auth/firebase.database",
                                  "https://www.googleapis.com/auth/userinfo.email");
            }
            else
            {
                // En local : lire depuis le fichier
                credential = GoogleCredential
                    .FromFile("firebase-service-account.json")
                    .CreateScoped("https://www.googleapis.com/auth/firebase.database",
                                  "https://www.googleapis.com/auth/userinfo.email");
            }

            return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        }

        // Enregistrer la machine dans Firebase (authentifié)
        private async Task EnregistrerMachineAsync(string email, string machineId, string machineName)
        {
            try
            {
                string emailKey = email.Replace(".", "_").Replace("@", "_at_");
                string accessToken = await GetServiceAccountTokenAsync();

                var urlId   = $"{_dbUrl}/licences/{emailKey}/machineId.json?access_token={accessToken}";
                var urlName = $"{_dbUrl}/licences/{emailKey}/machineName.json?access_token={accessToken}";

                await _http.PutAsJsonAsync(urlId,   machineId);
                await _http.PutAsJsonAsync(urlName, machineName);
            }
            catch { }
        }

        // ── 3. Vérification complète : token + licence + machine ──────────
        public async Task<LicenceCheckResponse> VerifierLicenceAsync(LicenceCheckRequest req)
        {
            // Étape 1 : vérifier le token Firebase
            var firebaseToken = await VerifyFirebaseTokenAsync(req.IdToken);
            if (firebaseToken == null)
                return Refus("Token Firebase invalide ou expiré");

            string email = firebaseToken.Claims.ContainsKey("email")
                ? firebaseToken.Claims["email"].ToString()!
                : "";

            if (string.IsNullOrEmpty(email))
                return Refus("Email non trouvé dans le token");

            // Étape 2 : récupérer la licence
            var licence = await GetLicenceByEmailAsync(email);
            if (licence == null)
            {
                string emailKey = email.Replace(".", "_").Replace("@", "_at_");
                return Refus($"Aucune licence trouvée pour {email} (clé: {emailKey}). Vérifiez le dashboard admin.");
            }

            if (!licence.Actif)
                return Refus("Licence désactivée. Contactez Maghreb Butus.");

            // Étape 3 : vérifier l'expiration
            if (DateTime.TryParse(licence.Expiration, out var expDate) && DateTime.UtcNow > expDate)
                return Refus($"Licence expirée le {expDate:dd/MM/yyyy}");

            // Étape 4 : vérification monoposte
            if (!string.IsNullOrEmpty(licence.MachineId))
            {
                if (licence.MachineId != req.MachineId)
                    return Refus($"Cette licence est liée à une autre machine. Contactez Maghreb Butus pour transférer.");
            }
            else
            {
                // Première activation : enregistrer la machine
                await EnregistrerMachineAsync(email, req.MachineId, req.MachineName);
            }

            // Étape 5 : générer un token de session signé
            string sessionToken = GenererSessionToken(email, req.MachineId, licence.Fonctions);

            return new LicenceCheckResponse
            {
                Autorise = true,
                Message = $"Bienvenue, {licence.Utilisateur}",
                Utilisateur = licence.Utilisateur,
                Expiration = licence.Expiration,
                Fonctions = licence.Fonctions,
                SessionToken = sessionToken
            };
        }

        // ── 4. Vérifier un token de session pour les appels API ───────────
        public ClaimsPrincipal? VerifierSessionToken(string sessionToken)
        {
            try
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_sessionSecret));
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(sessionToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = true,
                    ValidIssuer = "MaghrebButusAPI",
                    ValidateAudience = true,
                    ValidAudience = "MaghrebButusPlugin",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                }, out _);
                return principal;
            }
            catch
            {
                return null;
            }
        }

        // Vérifie si le token de session autorise une fonction
        public bool TokenAutoriseFonction(ClaimsPrincipal principal, string fonction)
        {
            var fonctions = principal.FindAll("fonction").Select(c => c.Value).ToList();
            return fonctions.Contains(fonction, StringComparer.OrdinalIgnoreCase);
        }

        // ── Helpers privés ────────────────────────────────────────────────

        private string GenererSessionToken(string email, string machineId, List<string> fonctions)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_sessionSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, email),
                new Claim("machineId", machineId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            // Ajouter chaque fonction comme claim
            foreach (var f in fonctions)
                claims.Add(new Claim("fonction", f));

            var token = new JwtSecurityToken(
                issuer: "MaghrebButusAPI",
                audience: "MaghrebButusPlugin",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),   // Session de 8h
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static LicenceCheckResponse Refus(string message) =>
            new LicenceCheckResponse { Autorise = false, Message = message };
    }
}
