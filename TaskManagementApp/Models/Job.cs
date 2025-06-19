using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace TaskManagementApp.Models
{
    public class Job
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "タイトルは必須です")]
        [StringLength(200, ErrorMessage = "タイトルは200文字以内で入力してください")]
        public string Title { get; set; } = string.Empty;
        
        [StringLength(1000, ErrorMessage = "説明は1000文字以内で入力してください")]
        public string Description { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "担当者は必須です")]
        public int AssignedUserId { get; set; }
        public User? AssignedUser { get; set; }
        
        public int? ParentJobId { get; set; }
        public Job? ParentJob { get; set; }
        public ICollection<Job> SubJobs { get; set; } = new List<Job>();
        
        [Required(ErrorMessage = "ステータスは必須です")]
        [StringLength(50, ErrorMessage = "ステータスは50文字以内で入力してください")]
        public string Status { get; set; } = "未着手"; // 未着手, 進行中, 完了
        
        [Required(ErrorMessage = "予定開始日時は必須です")]
        public DateTime ScheduledStart { get; set; }
        
        [Required(ErrorMessage = "予定終了日時は必須です")]
        public DateTime ScheduledEnd { get; set; }
        
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualEnd { get; set; }
        
        [Range(0, 1000, ErrorMessage = "予定時間は0-1000時間の範囲で入力してください")]
        public double ScheduledHours { get; set; }
        
        [Range(0, 1000, ErrorMessage = "実績時間は0-1000時間の範囲で入力してください")]
        public double ActualHours { get; set; }
        
        // 作業時間記録との関連
        public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
        
        // 計算プロパティ：実際の作業時間の合計
        public double TotalActualHours => TimeEntries?.Sum(te => te.ActualWorkHours) ?? 0;

        [StringLength(20, ErrorMessage = "優先度は20文字以内で入力してください")]
        public string Priority { get; set; } = "中"; // 低・中・高

        [StringLength(50, ErrorMessage = "カテゴリは50文字以内で入力してください")]
        public string? Category { get; set; } = "";
    }
} 