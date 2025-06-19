// ガントチャート画面用JavaScript

let ganttChart = null;
let ganttDataTable = null;
let jobsData = [];
let filteredJobsData = [];
let isInitialized = false;
let currentStatusFilter = 'all';
let currentDateRange = { start: null, end: null };

// Google Charts APIの初期化
function initGoogleCharts() {
    console.log('Google Charts API初期化開始');
    google.charts.load('current', {'packages':['timeline']});
    google.charts.setOnLoadCallback(function() { 
        console.log('Google Charts API読み込み完了');
        // データが準備できてからチャートを描画
        if (jobsData && jobsData.length > 0) {
            console.log('データが利用可能、チャート描画開始');
            applyFiltersAndDrawChart();
        } else {
            console.log('データがまだ利用不可、再試行をスケジュール');
            // データがまだない場合は少し待ってから再試行
            setTimeout(() => {
                if (jobsData && jobsData.length > 0) {
                    console.log('再試行: データが利用可能、チャート描画開始');
                    applyFiltersAndDrawChart();
                } else {
                    console.log('再試行: データなし、メッセージ表示');
                    showNoDataMessage();
                }
            }, 1000);
        }
    });
}

// フィルターを適用してチャートを描画
function applyFiltersAndDrawChart() {
    console.log('フィルター適用開始');
    
    // ステータスフィルターを適用
    if (currentStatusFilter === 'all') {
        filteredJobsData = [...jobsData];
    } else {
        filteredJobsData = jobsData.filter(job => job.status === currentStatusFilter);
    }
    
    console.log('ステータスフィルター適用後:', filteredJobsData.length, '件');
    
    // 日付範囲フィルターを適用
    if (currentDateRange.start || currentDateRange.end) {
        filteredJobsData = filteredJobsData.filter(job => {
            const jobStart = new Date(job.start);
            const jobEnd = new Date(job.end);
            
            if (currentDateRange.start && jobEnd < new Date(currentDateRange.start)) {
                return false;
            }
            if (currentDateRange.end && jobStart > new Date(currentDateRange.end)) {
                return false;
            }
            return true;
        });
    }
    
    console.log('日付範囲フィルター適用後:', filteredJobsData.length, '件');
    
    // チャートを描画
    drawChart();
}

// ステータスフィルター設定
function setStatusFilter(status) {
    console.log('ステータスフィルター設定:', status);
    currentStatusFilter = status;
    
    // ボタンのアクティブ状態を更新
    document.querySelectorAll('.btn-filter').forEach(btn => {
        btn.classList.remove('active');
    });
    document.querySelector(`[data-status="${status}"]`).classList.add('active');
    
    // フィルターを適用してチャートを再描画
    applyFiltersAndDrawChart();
}

// 日付範囲更新
function updateDateRange() {
    const startDate = document.getElementById('startDate').value;
    const endDate = document.getElementById('endDate').value;
    
    console.log('日付範囲更新:', startDate, '～', endDate);
    
    // 日付の妥当性チェック
    if (startDate && endDate && startDate > endDate) {
        alert('開始日は終了日より前の日付を設定してください。');
        return;
    }
    
    currentDateRange = {
        start: startDate || null,
        end: endDate || null
    };
    
    // フィルターを適用してチャートを再描画
    applyFiltersAndDrawChart();
}

// 日付範囲リセット
function resetDateRange() {
    console.log('日付範囲リセット');
    document.getElementById('startDate').value = '';
    document.getElementById('endDate').value = '';
    currentDateRange = { start: null, end: null };
    
    // フィルターを適用してチャートを再描画
    applyFiltersAndDrawChart();
}

// データなしメッセージ表示
function showNoDataMessage() {
    const container = document.getElementById('gantt');
    if (container) {
        let message = '表示するジョブデータがありません';
        if (currentStatusFilter !== 'all' || currentDateRange.start || currentDateRange.end) {
            message = 'フィルター条件に一致するジョブデータがありません';
        }
        
        container.innerHTML = `
            <div class="gantt-loading">
                <div class="gantt-loading-icon">
                    <i class="fas fa-inbox"></i>
                </div>
                <div>${message}</div>
                <div style="margin-top: 1rem;">
                    <button class="btn btn-primary" onclick="resetAllFilters()">
                        <i class="fas fa-undo"></i> フィルターをリセット
                    </button>
                    <a href="/Job/Create" class="btn btn-secondary" style="margin-left: 0.5rem;">
                        <i class="fas fa-plus"></i> 新しいジョブを作成
                    </a>
                </div>
            </div>
        `;
    }
}

// 全フィルターリセット
function resetAllFilters() {
    console.log('全フィルターリセット');
    
    // ステータスフィルターをリセット
    currentStatusFilter = 'all';
    document.querySelectorAll('.btn-filter').forEach(btn => {
        btn.classList.remove('active');
    });
    document.querySelector('[data-status="all"]').classList.add('active');
    
    // 日付範囲をリセット
    resetDateRange();
    
    // チャートを再描画
    applyFiltersAndDrawChart();
}

// ガントチャートの描画
function drawChart() {
    console.log('drawChart開始, filteredJobsData.length:', filteredJobsData ? filteredJobsData.length : 0);
    
    var container = document.getElementById('gantt');
    if (!container) {
        console.error('ganttコンテナが見つかりません');
        return;
    }
    
    // データチェック
    if (!filteredJobsData || filteredJobsData.length === 0) {
        console.log('フィルタリング後データなし、メッセージ表示');
        showNoDataMessage();
        return;
    }
    
    // ローディング表示
    showLoading();
    
    try {
        console.log('Google Charts Timeline作成開始');
        var chart = new google.visualization.Timeline(container);
        var dataTable = new google.visualization.DataTable();
        
        // データテーブルの列を設定
        dataTable.addColumn({ type: 'string', id: 'Status' });
        dataTable.addColumn({ type: 'string', id: 'Job' });
        dataTable.addColumn({ type: 'string', role: 'tooltip', p: { html: true } });
        dataTable.addColumn({ type: 'date', id: 'Start' });
        dataTable.addColumn({ type: 'date', id: 'End' });
        dataTable.addColumn({ type: 'string', role: 'style' });
        
        // ステータス別の色マッピング
        var statusColors = {
            "未着手": "#808080",  // 灰色
            "進行中": "#FFD700",  // 黄色
            "完了": "#4169E1"     // 青色
        };
        
        console.log('データテーブルにフィルタリング済みジョブデータを追加中...');
        // フィルタリング済みジョブデータをデータテーブルに追加
        filteredJobsData.forEach(function(job, index) {
            console.log(`フィルタリング済みジョブ${index + 1}:`, job.title, job.status, job.start, job.end);
            dataTable.addRow([
                job.status,
                '#' + job.id + ' ' + job.title + '（' + job.assignee + '）',
                createTooltip(job),
                new Date(job.start),
                new Date(job.end),
                'color: ' + statusColors[job.status] + '; cursor: pointer; border-radius: 4px;'
            ]);
        });
        
        // チャートオプションを設定
        let options = {
            timeline: { 
                groupByRowLabel: true,
                showRowLabels: true,
                showBarLabels: true
            },
            tooltip: { 
                isHtml: true,
                trigger: 'focus'
            },
            avoidOverlappingGridLines: false,
            height: 600,
            backgroundColor: 'transparent',
            chartArea: {
                backgroundColor: 'transparent'
            }
        };
        
        // 日付範囲が設定されている場合は表示範囲を制限
        if (currentDateRange.start || currentDateRange.end) {
            options.hAxis = {
                minValue: currentDateRange.start ? new Date(currentDateRange.start) : null,
                maxValue: currentDateRange.end ? new Date(currentDateRange.end) : null
            };
        }
        
        console.log('チャート描画実行');
        // チャートを描画
        chart.draw(dataTable, options);
        
        // クリックイベントを追加
        google.visualization.events.addListener(chart, 'select', function() {
            var selection = chart.getSelection();
            if (selection.length > 0) {
                var row = selection[0].row;
                var jobId = filteredJobsData[row].id;
                // ジョブ詳細画面に遷移
                window.location.href = '/Job/Details/' + jobId;
            }
        });
        
        // グローバル変数に保存
        window.ganttChart = chart;
        window.ganttDataTable = dataTable;
        
        // ローディングを非表示
        hideLoading();
        
        // 統計情報を更新
        updateStats();
        
        console.log('ガントチャート描画完了');
        
    } catch (error) {
        console.error('ガントチャート描画エラー:', error);
        showErrorMessage();
    }
}

// エラーメッセージ表示
function showErrorMessage() {
    const container = document.getElementById('gantt');
    if (container) {
        container.innerHTML = `
            <div class="gantt-loading">
                <div class="gantt-loading-icon">
                    <i class="fas fa-exclamation-triangle"></i>
                </div>
                <div>ガントチャートの表示中にエラーが発生しました</div>
                <div style="margin-top: 1rem;">
                    <button onclick="location.reload()" class="btn btn-primary">
                        <i class="fas fa-redo"></i> 再読み込み
                    </button>
                </div>
            </div>
        `;
    }
}

// ツールチップの作成
function createTooltip(job) {
    return `
        <div style="padding: 10px; max-width: 300px;">
            <div style="font-weight: bold; font-size: 14px; margin-bottom: 8px; color: #333;">
                ${job.title}
            </div>
            <div style="font-size: 12px; line-height: 1.4; color: #666;">
                <div><strong>担当:</strong> ${job.assignee}</div>
                <div><strong>期間:</strong> ${formatDate(job.start)} ～ ${formatDate(job.end)}</div>
                <div><strong>進捗:</strong> <span style="color: ${getStatusColor(job.status)};">${job.status}</span></div>
                <div><strong>優先度:</strong> ${job.priority}</div>
                <div><strong>カテゴリ:</strong> ${job.category}</div>
            </div>
            <div style="margin-top: 8px; font-size: 11px; color: #999; font-style: italic;">
                クリックで詳細表示
            </div>
        </div>
    `;
}

// ステータス色の取得
function getStatusColor(status) {
    const colors = {
        "未着手": "#808080",
        "進行中": "#FFD700", 
        "完了": "#4169E1"
    };
    return colors[status] || "#666";
}

// 日付フォーマット
function formatDate(dateStr) {
    var d = new Date(dateStr);
    return d.getFullYear() + '/' + (d.getMonth()+1) + '/' + d.getDate();
}

// 今日に移動
function moveToToday() {
    // ボタンにローディング効果を追加
    const btn = document.querySelector('.btn-today');
    const originalText = btn.innerHTML;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> 更新中...';
    btn.disabled = true;
    
    // 少し遅延を入れてからリロード
    setTimeout(() => {
        location.reload();
    }, 500);
}

// 統計情報の更新
function updateStats() {
    const stats = {
        total: filteredJobsData.length,
        notStarted: filteredJobsData.filter(job => job.status === '未着手').length,
        inProgress: filteredJobsData.filter(job => job.status === '進行中').length,
        completed: filteredJobsData.filter(job => job.status === '完了').length
    };
    
    console.log('フィルタリング後統計情報更新:', stats);
    
    // 統計カードを更新
    updateStatCard('total', stats.total, 'fas fa-tasks');
    updateStatCard('not-started', stats.notStarted, 'fas fa-clock');
    updateStatCard('in-progress', stats.inProgress, 'fas fa-play');
    updateStatCard('completed', stats.completed, 'fas fa-check-circle');
}

// 統計カードの更新
function updateStatCard(id, value, icon) {
    const card = document.getElementById(`stat-${id}`);
    if (card) {
        const valueElement = card.querySelector('.stat-value');
        if (valueElement) {
            valueElement.textContent = value;
        }
    }
}

// ローディング表示
function showLoading() {
    const container = document.getElementById('gantt');
    if (container) {
        container.innerHTML = `
            <div class="gantt-loading">
                <div class="gantt-loading-icon">
                    <i class="fas fa-chart-bar"></i>
                </div>
                <div>ガントチャートを読み込み中...</div>
            </div>
        `;
    }
}

// ローディング非表示
function hideLoading() {
    // ローディング表示は drawChart 内で自動的に削除される
}

// 統計カードのHTML生成
function createStatsHTML() {
    return `
        <div class="gantt-stats">
            <div class="stat-card" id="stat-total">
                <div class="stat-title">
                    <i class="fas fa-tasks"></i>
                    表示ジョブ数
                </div>
                <div class="stat-value">0</div>
            </div>
            <div class="stat-card not-started" id="stat-not-started">
                <div class="stat-title">
                    <i class="fas fa-clock"></i>
                    未着手
                </div>
                <div class="stat-value">0</div>
            </div>
            <div class="stat-card in-progress" id="stat-in-progress">
                <div class="stat-title">
                    <i class="fas fa-play"></i>
                    進行中
                </div>
                <div class="stat-value">0</div>
            </div>
            <div class="stat-card completed" id="stat-completed">
                <div class="stat-title">
                    <i class="fas fa-check-circle"></i>
                    完了
                </div>
                <div class="stat-value">0</div>
            </div>
        </div>
    `;
}

// レジェンドのHTML生成
function createLegendHTML() {
    return `
        <div class="gantt-legend">
            <div class="legend-title">
                <i class="fas fa-palette"></i>
                ステータス凡例
            </div>
            <div class="legend-items">
                <div class="legend-item">
                    <div class="legend-color" style="background-color: #808080;"></div>
                    <div class="legend-text">未着手</div>
                </div>
                <div class="legend-item">
                    <div class="legend-color" style="background-color: #FFD700;"></div>
                    <div class="legend-text">進行中</div>
                </div>
                <div class="legend-item">
                    <div class="legend-color" style="background-color: #4169E1;"></div>
                    <div class="legend-text">完了</div>
                </div>
            </div>
        </div>
    `;
}

// データ初期化関数
function initializeGanttData(data) {
    console.log('initializeGanttData開始, データ件数:', data ? data.length : 0);
    
    if (isInitialized) {
        console.log('既に初期化済み');
        return;
    }
    
    jobsData = data || [];
    console.log('ガントチャートデータ初期化:', jobsData.length, '件のジョブ');
    
    // 日付範囲の初期値を設定（今日から1ヶ月後まで）
    const today = new Date();
    const oneMonthLater = new Date();
    oneMonthLater.setMonth(today.getMonth() + 1);
    
    document.getElementById('startDate').value = today.toISOString().split('T')[0];
    document.getElementById('endDate').value = oneMonthLater.toISOString().split('T')[0];
    
    // 初期日付範囲を設定
    currentDateRange = {
        start: today.toISOString().split('T')[0],
        end: oneMonthLater.toISOString().split('T')[0]
    };
    
    // 統計情報とレジェンドを追加
    const ganttBody = document.querySelector('.gantt-body');
    if (ganttBody) {
        // 統計情報をチャートの前に挿入
        const chartContainer = ganttBody.querySelector('.gantt-chart-container');
        if (chartContainer) {
            chartContainer.insertAdjacentHTML('beforebegin', createStatsHTML());
        }
        
        // レジェンドをチャートの後に追加
        ganttBody.insertAdjacentHTML('beforeend', createLegendHTML());
    }
    
    isInitialized = true;
    
    // Google Charts APIを初期化
    initGoogleCharts();
}

// ページ読み込み時の初期化
document.addEventListener('DOMContentLoaded', function() {
    console.log('DOMContentLoaded: ガントチャート初期化開始');
    
    // データが既に設定されている場合は初期化を実行
    if (window.jobsData) {
        console.log('window.jobsDataが利用可能');
        initializeGanttData(window.jobsData);
    } else {
        console.log('window.jobsDataがまだ利用不可、再試行をスケジュール');
        // データがまだない場合は少し待ってから再試行
        setTimeout(() => {
            if (window.jobsData) {
                console.log('再試行: window.jobsDataが利用可能');
                initializeGanttData(window.jobsData);
            } else {
                console.warn('再試行: ガントチャートデータが見つかりません');
                showNoDataMessage();
            }
        }, 500);
    }
}); 