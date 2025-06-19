using Microsoft.AspNetCore.Mvc;
using TaskManagementApp.Models;
using Microsoft.EntityFrameworkCore;

namespace TaskManagementApp.Controllers
{
    public class TimeEntryController : Controller
    {
        private readonly AppDbContext _context;

        public TimeEntryController(AppDbContext context)
        {
            _context = context;
        }

        // 作業時間記録一覧表示
        public async Task<IActionResult> Index(int? jobId = null)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var query = _context.TimeEntries
                .Include(te => te.Job)
                .Include(te => te.User)
                .AsQueryable();

            // ジョブIDが指定されている場合はそのジョブの記録のみ
            if (jobId.HasValue)
            {
                query = query.Where(te => te.JobId == jobId.Value);
            }

            // 管理者以外は自分の記録のみ表示
            if (userRole != "Admin")
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
                if (user != null)
                {
                    query = query.Where(te => te.UserId == user.Id);
                }
            }

            var timeEntries = await query
                .OrderByDescending(te => te.StartTime)
                .ToListAsync();

            ViewBag.JobId = jobId;
            if (jobId.HasValue)
            {
                var job = await _context.Jobs.FindAsync(jobId.Value);
                ViewBag.JobTitle = job?.Title;
            }

            return View(timeEntries);
        }

        // 作業時間記録作成画面表示
        public async Task<IActionResult> Create(int? jobId = null)
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var timeEntry = new TimeEntry
            {
                UserId = user.Id,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now.AddHours(1)
            };

            if (jobId.HasValue)
            {
                timeEntry.JobId = jobId.Value;
                var job = await _context.Jobs.FindAsync(jobId.Value);
                ViewBag.JobTitle = job?.Title;
            }

            ViewBag.Jobs = await _context.Jobs
                .Where(j => j.AssignedUserId == user.Id || (j.AssignedUser != null && j.AssignedUser.Name == userName))
                .ToListAsync();

            return View(timeEntry);
        }

        // 作業時間記録作成処理
        [HttpPost]
        public async Task<IActionResult> Create(TimeEntry timeEntry)
        {
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            // UserIdをセッションから自動設定
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Name == userName);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }
            timeEntry.UserId = user.Id;

            if (ModelState.IsValid)
            {
                // 開始時間が終了時間より後でないかチェック
                if (timeEntry.StartTime >= timeEntry.EndTime)
                {
                    ModelState.AddModelError("EndTime", "終了時間は開始時間より後である必要があります。");
                    ViewBag.Jobs = await _context.Jobs
                        .Where(j => j.AssignedUserId == user.Id || (j.AssignedUser != null && j.AssignedUser.Name == userName))
                        .ToListAsync();
                    return View(timeEntry);
                }

                // 休憩時間が作業時間を超えていないかチェック
                var totalWorkTime = (timeEntry.EndTime - timeEntry.StartTime).TotalHours;
                if (timeEntry.BreakTime > totalWorkTime)
                {
                    ModelState.AddModelError("BreakTime", "休憩時間は作業時間を超えることはできません。");
                    ViewBag.Jobs = await _context.Jobs
                        .Where(j => j.AssignedUserId == user.Id || (j.AssignedUser != null && j.AssignedUser.Name == userName))
                        .ToListAsync();
                    return View(timeEntry);
                }

                // 日付をUTCに変換（Unspecified→UTC）
                timeEntry.StartTime = DateTime.SpecifyKind(timeEntry.StartTime, DateTimeKind.Unspecified).ToUniversalTime();
                timeEntry.EndTime = DateTime.SpecifyKind(timeEntry.EndTime, DateTimeKind.Unspecified).ToUniversalTime();
                timeEntry.CreatedAt = DateTime.UtcNow;

                _context.TimeEntries.Add(timeEntry);
                await _context.SaveChangesAsync();

                return RedirectToAction("Index", new { jobId = timeEntry.JobId });
            }

            // バリデーションエラー時もジョブ選択肢を再設定
            ViewBag.Jobs = await _context.Jobs
                .Where(j => j.AssignedUserId == user.Id || (j.AssignedUser != null && j.AssignedUser.Name == userName))
                .ToListAsync();
            return View(timeEntry);
        }

        // 作業時間記録編集画面表示
        public async Task<IActionResult> Edit(int id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var timeEntry = await _context.TimeEntries
                .Include(te => te.Job)
                .Include(te => te.User)
                .FirstOrDefaultAsync(te => te.Id == id);

            if (timeEntry == null)
            {
                return NotFound();
            }

            // 権限チェック：管理者または記録者本人のみ編集可能
            if (userRole != "Admin" && (timeEntry.User?.Name ?? "") != userName)
            {
                return RedirectToAction("Index");
            }

            ViewBag.Jobs = await _context.Jobs.ToListAsync();
            return View(timeEntry);
        }

        // 作業時間記録編集処理
        [HttpPost]
        public async Task<IActionResult> Edit(int id, TimeEntry timeEntry)
        {
            if (id != timeEntry.Id)
            {
                return NotFound();
            }

            var userName = HttpContext.Session.GetString("UserName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var existingEntry = await _context.TimeEntries
                .Include(te => te.User)
                .FirstOrDefaultAsync(te => te.Id == id);

            if (existingEntry == null)
            {
                return NotFound();
            }

            // 権限チェック
            if (userRole != "Admin" && (existingEntry.User?.Name ?? "") != userName)
            {
                return RedirectToAction("Index");
            }

            if (ModelState.IsValid)
            {
                // 開始時間が終了時間より後でないかチェック
                if (timeEntry.StartTime >= timeEntry.EndTime)
                {
                    ModelState.AddModelError("EndTime", "終了時間は開始時間より後である必要があります。");
                    ViewBag.Jobs = await _context.Jobs.ToListAsync();
                    return View(timeEntry);
                }

                // 休憩時間が作業時間を超えていないかチェック
                var totalWorkTime = (timeEntry.EndTime - timeEntry.StartTime).TotalHours;
                if (timeEntry.BreakTime > totalWorkTime)
                {
                    ModelState.AddModelError("BreakTime", "休憩時間は作業時間を超えることはできません。");
                    ViewBag.Jobs = await _context.Jobs.ToListAsync();
                    return View(timeEntry);
                }

                // 日付をUTCに変換（Unspecified→UTC）
                timeEntry.StartTime = DateTime.SpecifyKind(timeEntry.StartTime, DateTimeKind.Unspecified).ToUniversalTime();
                timeEntry.EndTime = DateTime.SpecifyKind(timeEntry.EndTime, DateTimeKind.Unspecified).ToUniversalTime();
                timeEntry.UpdatedAt = DateTime.UtcNow;
                existingEntry.StartTime = DateTime.SpecifyKind(timeEntry.StartTime, DateTimeKind.Unspecified).ToUniversalTime();
                existingEntry.EndTime = DateTime.SpecifyKind(timeEntry.EndTime, DateTimeKind.Unspecified).ToUniversalTime();
                existingEntry.UpdatedAt = DateTime.UtcNow;
                existingEntry.BreakTime = timeEntry.BreakTime;
                existingEntry.WorkDescription = timeEntry.WorkDescription;

                await _context.SaveChangesAsync();
                return RedirectToAction("Index", new { jobId = timeEntry.JobId });
            }

            ViewBag.Jobs = await _context.Jobs.ToListAsync();
            return View(timeEntry);
        }

        // 作業時間記録削除処理
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userName = HttpContext.Session.GetString("UserName");
            var userRole = HttpContext.Session.GetString("UserRole");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var timeEntry = await _context.TimeEntries
                .Include(te => te.User)
                .FirstOrDefaultAsync(te => te.Id == id);

            if (timeEntry == null)
            {
                return NotFound();
            }

            // 権限チェック：管理者または記録者本人のみ削除可能
            if (userRole != "Admin" && (timeEntry.User?.Name ?? "") != userName)
            {
                return RedirectToAction("Index");
            }

            _context.TimeEntries.Remove(timeEntry);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { jobId = timeEntry.JobId });
        }
    }
} 