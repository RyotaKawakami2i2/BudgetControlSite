using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TaskManagementApp.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty; // Admin, Leader, User

        public ICollection<TeamUser> TeamUsers { get; set; } = new List<TeamUser>();

        // パスワードリセット用
        public string? ResetPasswordToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
    }
} 