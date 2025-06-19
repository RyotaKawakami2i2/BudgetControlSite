using Microsoft.AspNetCore.Mvc;
using TaskManagementApp.Models;
using Microsoft.EntityFrameworkCore;

namespace TaskManagementApp.Controllers
{
    public class TeamController : Controller
    {
        private readonly AppDbContext _context;

        public TeamController(AppDbContext context)
        {
            _context = context;
        }

        // チーム一覧表示
        public async Task<IActionResult> Index()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            List<Team> teams;
            if (userRole == "Admin")
            {
                // 管理者は全チームを表示
                teams = await _context.Teams
                    .Include(t => t.TeamUsers)
                    .ThenInclude(tu => tu.User)
                    .ToListAsync();
            }
            else
            {
                // 一般ユーザーは所属チームのみ表示
                teams = await _context.Teams
                    .Include(t => t.TeamUsers)
                    .ThenInclude(tu => tu.User)
                    .Where(t => t.TeamUsers.Any(tu => tu.User != null && tu.User.Name == userName))
                    .ToListAsync();
            }

            return View(teams);
        }

        // チーム作成画面表示
        public async Task<IActionResult> Create()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (userRole != "Admin")
            {
                return RedirectToAction("Index");
            }
            // リーダー候補（LeaderまたはAdmin）を取得
            ViewBag.LeaderCandidates = await _context.Users
                .Where(u => u.Role == "Leader" || u.Role == "Admin")
                .ToListAsync();
            return View(new Team());
        }

        // チーム作成処理
        [HttpPost]
        public async Task<IActionResult> Create(Team team)
        {
            if (ModelState.IsValid)
            {
                _context.Teams.Add(team);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            // バリデーションエラー時もリーダー候補を再設定
            ViewBag.LeaderCandidates = await _context.Users
                .Where(u => u.Role == "Leader" || u.Role == "Admin")
                .ToListAsync();
            return View(team);
        }

        // チーム編集画面表示
        public async Task<IActionResult> Edit(int id)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (userRole != "Admin")
            {
                return RedirectToAction("Index");
            }

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .ThenInclude(tu => tu.User)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (team == null)
            {
                return NotFound();
            }

            return View(team);
        }

        // チーム編集処理
        [HttpPost]
        public async Task<IActionResult> Edit(int id, Team team)
        {
            if (id != team.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(team);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TeamExists(team.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction("Index");
            }
            return View(team);
        }

        // チーム削除処理
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            if (userRole != "Admin")
            {
                return RedirectToAction("Index");
            }

            var team = await _context.Teams.FindAsync(id);
            if (team != null)
            {
                _context.Teams.Remove(team);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        // チームメンバー管理画面表示
        public async Task<IActionResult> Members(int id)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .ThenInclude(tu => tu.User)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (team == null)
            {
                return NotFound();
            }

            // 権限チェック：管理者またはチームリーダーのみアクセス可能
            if (userRole != "Admin" && (team.TeamLeader ?? "") != userName)
            {
                return RedirectToAction("Index");
            }

            ViewBag.TeamId = id;
            ViewBag.TeamName = team.Name ?? "";
            ViewBag.AvailableUsers = await _context.Users
                .Where(u => !u.TeamUsers.Any(tu => tu.TeamId == id))
                .ToListAsync();

            return View(team);
        }

        // チームメンバー追加処理
        [HttpPost]
        public async Task<IActionResult> AddMember(int teamId, int userId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .FirstOrDefaultAsync(t => t.Id == teamId);

            if (team == null)
            {
                return NotFound();
            }

            // 権限チェック
            if (userRole != "Admin" && team.TeamLeader != userName)
            {
                return RedirectToAction("Index");
            }

            // 既にメンバーかチェック
            if (team.TeamUsers.Any(tu => tu.UserId == userId))
            {
                return RedirectToAction("Members", new { id = teamId });
            }

            var teamUser = new TeamUser
            {
                TeamId = teamId,
                UserId = userId
            };

            _context.TeamUsers.Add(teamUser);
            await _context.SaveChangesAsync();

            return RedirectToAction("Members", new { id = teamId });
        }

        // チームメンバー削除処理
        [HttpPost]
        public async Task<IActionResult> RemoveMember(int teamId, int userId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");

            if (string.IsNullOrEmpty(userName))
            {
                return RedirectToAction("Login", "Account");
            }

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .FirstOrDefaultAsync(t => t.Id == teamId);

            if (team == null)
            {
                return NotFound();
            }

            // 権限チェック
            if (userRole != "Admin" && team.TeamLeader != userName)
            {
                return RedirectToAction("Index");
            }

            var teamUser = await _context.TeamUsers
                .FirstOrDefaultAsync(tu => tu.TeamId == teamId && tu.UserId == userId);

            if (teamUser != null)
            {
                _context.TeamUsers.Remove(teamUser);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Members", new { id = teamId });
        }

        // チームメンバーのジョブ一覧・予定入力画面
        public async Task<IActionResult> MemberJobs(int teamId, int? userId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .ThenInclude(tu => tu.User)
                .FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null)
                return NotFound();

            // 権限チェック：管理者またはチームリーダーのみ
            if (userRole != "Admin" && team.TeamLeader != userName)
                return RedirectToAction("Index");

            // メンバーリスト
            var members = team.TeamUsers.Select(tu => tu.User).ToList();
            ViewBag.TeamId = teamId;
            ViewBag.TeamName = team.Name;
            ViewBag.Members = members;
            ViewBag.SelectedUserId = userId;

            // 選択メンバーのジョブ一覧
            List<Job> jobs = new List<Job>();
            if (userId.HasValue)
            {
                jobs = await _context.Jobs
                    .Where(j => j.AssignedUserId == userId.Value)
                    .OrderByDescending(j => j.ScheduledStart)
                    .ToListAsync();
            }
            return View(jobs);
        }

        // メンバーのジョブ新規登録（GET）
        public async Task<IActionResult> CreateMemberJob(int teamId, int userId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .ThenInclude(tu => tu.User)
                .FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null)
                return NotFound();

            // 権限チェック：管理者またはチームリーダーのみ
            if (userRole != "Admin" && team.TeamLeader != userName)
                return RedirectToAction("Index");

            var member = team.TeamUsers.FirstOrDefault(tu => tu.UserId == userId)?.User;
            if (member == null)
                return NotFound();

            ViewBag.TeamId = teamId;
            ViewBag.UserId = userId;
            ViewBag.UserName = member.Name;
            // ステータス・優先度リストはJobControllerと同じものを利用
            ViewBag.StatusList = new[] { "未着手", "進行中", "完了" };
            ViewBag.PriorityList = new[] { "低", "中", "高" };
            return View();
        }

        // メンバーのジョブ新規登録（POST）
        [HttpPost]
        public async Task<IActionResult> CreateMemberJob(int teamId, int userId, Job job)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userName = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrEmpty(userName))
                return RedirectToAction("Login", "Account");

            var team = await _context.Teams
                .Include(t => t.TeamUsers)
                .ThenInclude(tu => tu.User)
                .FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null)
                return NotFound();

            // 権限チェック：管理者またはチームリーダーのみ
            if (userRole != "Admin" && team.TeamLeader != userName)
                return RedirectToAction("Index");

            var member = team.TeamUsers.FirstOrDefault(tu => tu.UserId == userId)?.User;
            if (member == null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                // ステータス・優先度リスト再設定
                ViewBag.TeamId = teamId;
                ViewBag.UserId = userId;
                ViewBag.UserName = member.Name;
                ViewBag.StatusList = new[] { "未着手", "進行中", "完了" };
                ViewBag.PriorityList = new[] { "低", "中", "高" };
                return View(job);
            }

            // ジョブ登録
            job.AssignedUserId = userId;
            job.Status = job.Status ?? "未着手";
            job.Priority = job.Priority ?? "中";
            job.ScheduledStart = DateTime.SpecifyKind(job.ScheduledStart, DateTimeKind.Utc);
            job.ScheduledEnd = DateTime.SpecifyKind(job.ScheduledEnd, DateTimeKind.Utc);
            _context.Jobs.Add(job);
            await _context.SaveChangesAsync();
            return RedirectToAction("MemberJobs", new { teamId = teamId, userId = userId });
        }

        private bool TeamExists(int id)
        {
            return _context.Teams.Any(e => e.Id == id);
        }
    }
} 