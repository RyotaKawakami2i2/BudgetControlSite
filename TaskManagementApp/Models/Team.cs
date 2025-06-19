using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TaskManagementApp.Models
{
    public class Team
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "チーム名は必須です")]
        [StringLength(100, ErrorMessage = "チーム名は100文字以内で入力してください")]
        public string? Name { get; set; }
        
        [StringLength(100, ErrorMessage = "チームリーダー名は100文字以内で入力してください")]
        public string? TeamLeader { get; set; }
        
        [StringLength(500, ErrorMessage = "説明は500文字以内で入力してください")]
        public string? Description { get; set; }
        
        public ICollection<TeamUser> TeamUsers { get; set; } = new List<TeamUser>();
    }
} 