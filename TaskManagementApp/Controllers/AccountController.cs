using Microsoft.AspNetCore.Mvc;
using TaskManagementApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace TaskManagementApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher = new PasswordHasher<User>();
        private readonly IConfiguration _configuration;

        public AccountController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (HttpContext.Session.GetString("UserName") != null)
            {
                return RedirectToAction("Index", "Job");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user != null)
            {
                var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                if (result == PasswordVerificationResult.Success)
                {
                    HttpContext.Session.SetString("UserName", user.Name);
                    HttpContext.Session.SetString("UserRole", user.Role);
                    return RedirectToAction("Index", "Job");
                }
            }
            ViewBag.Error = "メールアドレスまたはパスワードが正しくありません。";
            return View();
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(string name, string email, string password)
        {
            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                ViewBag.Error = "既に登録済みのメールアドレスです。";
                return View();
            }
            
            var user = new User
            {
                Name = name,
                Email = email,
                Role = "User",
                PasswordHash = ""
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            
            ViewBag.Success = "アカウントが正常に作成されました。ログインしてください。";
            return RedirectToAction("Login");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Error = "該当するメールアドレスが見つかりません。";
                return View();
            }
            // トークン生成
            var token = Guid.NewGuid().ToString();
            user.ResetPasswordToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
            await _context.SaveChangesAsync();

            // リセットURL生成
            var resetUrl = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);
            var smtpSection = _configuration.GetSection("Smtp");
            var smtpHost = smtpSection.GetValue<string>("Host");
            var smtpPort = smtpSection.GetValue<int>("Port");
            var smtpFrom = smtpSection.GetValue<string>("From");

            // メール送信
            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.EnableSsl = false;
                var mail = new MailMessage(smtpFrom, user.Email)
                {
                    Subject = "パスワードリセットのご案内",
                    Body = $"パスワードリセットをご希望の場合は、以下のリンクをクリックしてください。\n{resetUrl}\n\nこのリンクは1時間有効です。"
                };
                await client.SendMailAsync(mail);
            }
            ViewBag.Message = "パスワードリセット用のメールを送信しました。";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login");
            }
            var user = await _context.Users.FirstOrDefaultAsync(u => u.ResetPasswordToken == token && u.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null)
            {
                ViewBag.Error = "無効または期限切れのトークンです。";
                return View();
            }
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string password)
        {
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login");
            }
            var user = await _context.Users.FirstOrDefaultAsync(u => u.ResetPasswordToken == token && u.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null)
            {
                ViewBag.Error = "無効または期限切れのトークンです。";
                return View();
            }
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            user.ResetPasswordToken = null;
            user.ResetTokenExpiry = null;
            await _context.SaveChangesAsync();
            ViewBag.Message = "パスワードがリセットされました。ログインしてください。";
            return RedirectToAction("Login");
        }
    }
} 