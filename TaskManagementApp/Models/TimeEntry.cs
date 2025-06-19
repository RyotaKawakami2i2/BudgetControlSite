using System;
using System.ComponentModel.DataAnnotations;

namespace TaskManagementApp.Models
{
    public class TimeEntry
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "ジョブは必須です")]
        public int JobId { get; set; }
        public Job? Job { get; set; }
        
        [Required(ErrorMessage = "ユーザーは必須です")]
        public int UserId { get; set; }
        public User? User { get; set; }
        
        [Required(ErrorMessage = "作業開始日時は必須です")]
        public DateTime StartTime { get; set; }
        
        [Required(ErrorMessage = "作業終了日時は必須です")]
        public DateTime EndTime { get; set; }
        
        [Range(0, 24, ErrorMessage = "休憩時間は0-24時間の範囲で入力してください")]
        public double BreakTime { get; set; } = 0;
        
        [StringLength(500, ErrorMessage = "作業内容は500文字以内で入力してください")]
        public string WorkDescription { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        
        // 計算プロパティ：実際の作業時間（休憩時間を除く）
        public double ActualWorkHours => (EndTime - StartTime).TotalHours - BreakTime;
    }
} 