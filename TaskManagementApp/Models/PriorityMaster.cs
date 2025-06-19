using System.ComponentModel.DataAnnotations;

namespace TaskManagementApp.Models
{
    public class PriorityMaster
    {
        public int Id { get; set; }
        [Required]
        [StringLength(20)]
        public string Name { get; set; } = string.Empty;
        [StringLength(100)]
        public string? Description { get; set; }
    }
} 