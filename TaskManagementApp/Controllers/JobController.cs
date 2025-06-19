using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TaskManagementApp.Models;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace TaskManagementApp.Controllers
{
    public class JobController : Controller
    {
        private readonly AppDbContext _context;
        // ステータス・優先度の選択肢リストを共通定義
        private static readonly string[] StatusList = new[] { "未着手", "進行中", "完了" };
        private static readonly string[] PriorityList = new[] { "低", "中", "高" };
        public JobController(AppDbContext context)
        {
            _context = context;
        }

        // ジョブ一覧
        public async Task<IActionResult> Index()
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null) return RedirectToAction("Login", "Account");

            var userRole = HttpContext.Session.GetString("UserRole");
            IQueryable<Job> query = _context.Jobs
                .Include(j => j.AssignedUser)
                .Include(j => j.ParentJob);

            if (userRole != "Admin" && userRole != "Leader")
            {
                query = query.Where(j => j.AssignedUserId == user.Id);
            }
            var jobs = await query.ToListAsync();
            ViewBag.StatusList = await _context.StatusMasters.Select(s => s.Name).ToListAsync();
            ViewBag.PriorityList = await _context.PriorityMasters.Select(p => p.Name).ToListAsync();
            return View(jobs);
        }

        // ジョブ詳細表示
        public async Task<IActionResult> Details(int id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");

            var job = await _context.Jobs
                .Include(j => j.AssignedUser)
                .Include(j => j.ParentJob)
                .Include(j => j.SubJobs)
                .Include(j => j.TimeEntries)
                .ThenInclude(te => te.User)
                .FirstOrDefaultAsync(j => j.Id == id);

            if (job == null)
                return NotFound();

            // 権限チェック：担当者または管理者のみアクセス可能
            var userRole = HttpContext.Session.GetString("UserRole");
            if (userRole != "Admin" && (job.AssignedUser?.Name ?? "") != userName)
                return RedirectToAction("Index");

            return View(job);
        }

        // ジョブ登録（GET）
        public async Task<IActionResult> Create()
        {
            var userName = HttpContext.Session.GetString("UserName");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null) return RedirectToAction("Login", "Account");
            ViewBag.ParentJobs = await _context.Jobs
                .Where(j => j.AssignedUserId == user.Id)
                .ToListAsync();
            ViewBag.StatusList = await _context.StatusMasters.Select(s => s.Name).ToListAsync();
            ViewBag.PriorityList = await _context.PriorityMasters.Select(p => p.Name).ToListAsync();
            return View(new Job());
        }

        // ジョブ登録（POST）
        [HttpPost]
        public async Task<IActionResult> Create(Job job)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null) return RedirectToAction("Login", "Account");
            job.AssignedUserId = user.Id;
            // 日付をUTCに変換
            job.ScheduledStart = DateTime.SpecifyKind(job.ScheduledStart, DateTimeKind.Utc);
            job.ScheduledEnd = DateTime.SpecifyKind(job.ScheduledEnd, DateTimeKind.Utc);
            if (job.ActualStart.HasValue)
                job.ActualStart = DateTime.SpecifyKind(job.ActualStart.Value, DateTimeKind.Utc);
            if (job.ActualEnd.HasValue)
                job.ActualEnd = DateTime.SpecifyKind(job.ActualEnd.Value, DateTimeKind.Utc);
            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        // ジョブ編集（GET）
        public async Task<IActionResult> Edit(int id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null) return RedirectToAction("Login", "Account");
            var job = await _context.Jobs.FindAsync(id);
            if (job == null) return NotFound();
            ViewBag.ParentJobs = await _context.Jobs.Where(j => j.AssignedUserId == user.Id && j.Id != id).ToListAsync();
            ViewBag.StatusList = await _context.StatusMasters.Select(s => s.Name).ToListAsync();
            ViewBag.PriorityList = await _context.PriorityMasters.Select(p => p.Name).ToListAsync();
            return View(job);
        }

        // ジョブ編集（POST）
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Job job)
        {
            if (id != job.Id) return NotFound();
            var userName = HttpContext.Session.GetString("UserName");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null) return RedirectToAction("Login", "Account");
            var existing = await _context.Jobs.FindAsync(id);
            if (existing == null) return NotFound();
            // 更新項目
            existing.Title = job.Title;
            existing.Description = job.Description;
            existing.ScheduledStart = DateTime.SpecifyKind(job.ScheduledStart, DateTimeKind.Utc);
            existing.ScheduledEnd = DateTime.SpecifyKind(job.ScheduledEnd, DateTimeKind.Utc);
            existing.ScheduledHours = job.ScheduledHours;
            existing.ParentJobId = job.ParentJobId;
            existing.Status = job.Status;
            existing.Priority = job.Priority;
            existing.Category = job.Category;
            await _context.SaveChangesAsync();
            return RedirectToAction("Details", new { id });
        }

        // ジョブ削除
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var job = await _context.Jobs.FindAsync(id);
            if (job != null)
            {
                _context.Jobs.Remove(job);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        // ステータス変更（POST）
        [HttpPost]
        public async Task<IActionResult> ChangeStatus(int id, string status)
        {
            var job = await _context.Jobs.FindAsync(id);
            if (job == null) return NotFound();
            job.Status = status;
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        // 優先度変更（POST）
        [HttpPost]
        public async Task<IActionResult> ChangePriority(int id, string priority)
        {
            var job = await _context.Jobs.FindAsync(id);
            if (job == null) return NotFound();
            job.Priority = priority;
            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Gantt()
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");
            var userRole = HttpContext.Session.GetString("UserRole");
            IQueryable<Job> query = _context.Jobs
                .Include(j => j.AssignedUser)
                .Include(j => j.ParentJob);
            // 管理者・リーダーは全ジョブ、一般ユーザーは自分のジョブのみ
            if (userRole != "Admin" && userRole != "Leader")
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
                if (user == null) return RedirectToAction("Login", "Account");
                query = query.Where(j => j.AssignedUserId == user.Id);
            }
            var jobs = await query.ToListAsync();
            return View(jobs);
        }
    }
} 