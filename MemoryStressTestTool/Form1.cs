using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices; // 需加入參考 Microsoft.VisualBasic

namespace MemoryStressTest
{
    public partial class Form1 : Form
    {
        // 核心變數
        private List<IntPtr> _memoryAllocations = new List<IntPtr>();
        private Timer _monitorTimer;
        private bool _isRunning = false;
        private const int CHUNK_SIZE_MB = 100; // 每次配置 100MB
        private long _targetMemoryMB = 4096;   // 預設 4GB
        private string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MemTest_Log.csv");

        // 紀錄最大值用
        private double _maxProcessMemory = 0;
        private double _maxSystemCommit = 0;
        private double _maxSwapUsage = 0;

        // UI 控件 (以程式碼動態生成，方便您直接複製貼上執行)
        private Label lblTarget;
        private TextBox txtTargetMB;
        private Button btnStart;
        private Button btnStop;
        private RichTextBox rtbLog;
        private GroupBox grpStatus;
        private Label lblProcessMem;
        private Label lblSystemPhysical;
        private Label lblSystemCommit; // 近似 SWAP/PageFile 總量
        
        public Form1()
        {
            InitializeComponent_Custom();
            InitializeLogic();
        }

        // 初始化邏輯與計時器
        private void InitializeLogic()
        {
            _monitorTimer = new Timer();
            _monitorTimer.Interval = 1000; // 1秒更新一次
            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start(); // 啟動監控，即使沒開始測試也要看數值
        }

        // 計時器觸發：更新 UI 與 寫 Log
        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            // 1. 取得目前 Process 資訊
            Process currentProc = Process.GetCurrentProcess();
            currentProc.Refresh();
            double myWorkingSetMB = currentProc.WorkingSet64 / 1024.0 / 1024.0; // 實體記憶體
            double myPrivateMB = currentProc.PrivateMemorySize64 / 1024.0 / 1024.0; // 提交記憶體 (含Swap)

            // 2. 取得系統整體資訊
            ComputerInfo compInfo = new ComputerInfo();
            double totalPhysMB = compInfo.TotalPhysicalMemory / 1024.0 / 1024.0;
            double availPhysMB = compInfo.AvailablePhysicalMemory / 1024.0 / 1024.0;
            double usedPhysMB = totalPhysMB - availPhysMB;

            // 3. 計算 SWAP / Commit 使用量 (使用 PerformanceCounter 較精準，但這裡用簡易算法)
            // System Commit Limit 大約等於 實體記憶體 + PageFile 
            // 這裡我們主要觀察 "系統已提交記憶體" vs "實體記憶體"
            // 若 Committed Bytes >> Physical Total，代表正在大量使用 SWAP
            double systemCommitTotalMB = GetSystemCommitTotal() / 1024.0 / 1024.0;
            
            // 4. 更新最大值紀錄
            if (myWorkingSetMB > _maxProcessMemory) _maxProcessMemory = myWorkingSetMB;
            if (systemCommitTotalMB > _maxSystemCommit) _maxSystemCommit = systemCommitTotalMB;
            
            // SWAP 推算：系統提交總量 - 實體記憶體總量 (若為正數，表示一定有部分在 PageFile)
            double estimatedSwapUsage = systemCommitTotalMB - totalPhysMB; 
            if (estimatedSwapUsage < 0) estimatedSwapUsage = 0;
            if (estimatedSwapUsage > _maxSwapUsage) _maxSwapUsage = estimatedSwapUsage;

            // 5. 更新 UI
            UpdateUI(myWorkingSetMB, usedPhysMB, totalPhysMB, estimatedSwapUsage, systemCommitTotalMB);

            // 6. 寫入 Log
            if (_isRunning)
            {
                LogData(myWorkingSetMB, usedPhysMB, totalPhysMB, estimatedSwapUsage);
            }

            // 7. 自動配置邏輯 (若正在執行測試)
            if (_isRunning)
            {
                AllocateMemoryStep();
            }
        }

        private void AllocateMemoryStep()
        {
            double currentAllocatedMB = (_memoryAllocations.Count * CHUNK_SIZE_MB);
            
            if (currentAllocatedMB < _targetMemoryMB)
            {
                try
                {
                    // 使用 Unmanaged Memory (AllocHGlobal) 比較能模擬底層壓力，且不會被 .NET GC 干擾
                    int size = CHUNK_SIZE_MB * 1024 * 1024;
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    
                    // 【關鍵】：必須寫入記憶體 (Touch)，OS 才會真正分配實體 RAM (Demand Paging)
                    // 如果只 Alloc 不 Write，Windows 智慧記憶體管理可能只會標記為 Virtual Memory 但不給實體 RAM
                    // 我們每隔 4KB (一個 Page) 寫入一個 byte
                    unsafe
                    {
                        byte* p = (byte*)ptr;
                        for (int i = 0; i < size; i += 4096)
                        {
                            p[i] = 0xFF; 
                        }
                    }

                    _memoryAllocations.Add(ptr);
                    rtbLog.AppendText($"[Info] 已增加 {CHUNK_SIZE_MB} MB. 目前程式持有: {currentAllocatedMB + CHUNK_SIZE_MB} MB\n");
                }
                catch (OutOfMemoryException)
                {
                    rtbLog.AppendText($"[Error] 記憶體不足 (OOM)！ 無法再配置。\n");
                    StopTest();
                }
                catch (Exception ex)
                {
                    rtbLog.AppendText($"[Error] 配置失敗: {ex.Message}\n");
                    StopTest();
                }
            }
        }

        private void UpdateUI(double myProcMem, double sysUsed, double sysTotal, double swapEst, double sysCommit)
        {
            lblProcessMem.Text = $"1. 本程式使用實體記憶體 (Working Set): {myProcMem:F0} MB (最大: {_maxProcessMemory:F0} MB)";
            lblSystemPhysical.Text = $"2. 系統實體記憶體使用: {sysUsed:F0} / {sysTotal:F0} MB";
            lblSystemCommit.Text = $"3. 系統 SWAP/PageFile 推算 (提交超量): {swapEst:F0} MB (最大: {_maxSwapUsage:F0} MB)\n    (系統總提交: {sysCommit:F0} MB)";
            
            // 視覺警示
            if (swapEst > 100) lblSystemCommit.ForeColor = System.Drawing.Color.Red;
            else lblSystemCommit.ForeColor = System.Drawing.Color.Black;
        }

        private void LogData(double myMem, double sysUsed, double sysTotal, double swap)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = $"{timestamp},{myMem:F0},{sysUsed:F0},{sysTotal:F0},{swap:F0}";
                
                // 如果檔案不存在，先寫 Header
                if (!File.Exists(_logFilePath))
                {
                    File.WriteAllText(_logFilePath, "Time,Process_WorkingSet_MB,System_Physical_Used_MB,System_Physical_Total_MB,System_Estimated_Swap_Used_MB\n");
                }
                
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { /* Log 失敗不影響主程式 */ }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (double.TryParse(txtTargetMB.Text, out double val))
            {
                _targetMemoryMB = (long)val;
            }
            else
            {
                MessageBox.Show("請輸入有效的數字");
                return;
            }

            _isRunning = true;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            txtTargetMB.Enabled = false;
            
            // 重置最大值
            _maxProcessMemory = 0;
            _maxSystemCommit = 0;
            _maxSwapUsage = 0;

            rtbLog.AppendText($"--- 開始測試，目標: {_targetMemoryMB} MB ---\n");
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopTest();
        }

        private void StopTest()
        {
            _isRunning = false;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            txtTargetMB.Enabled = true;

            rtbLog.AppendText("--- 停止測試，正在釋放記憶體... ---\n");

            // 釋放記憶體
            foreach (var ptr in _memoryAllocations)
            {
                Marshal.FreeHGlobal(ptr);
            }
            _memoryAllocations.Clear();
            
            // 強制 GC 以清理 managed resources
            GC.Collect();
            GC.WaitForPendingFinalizers();

            rtbLog.AppendText("--- 記憶體已釋放 ---\n");
        }

        // 取得系統提交記憶體 (Performance Counter)
        private PerformanceCounter _commitCounter;
        private float GetSystemCommitTotal()
        {
            if (_commitCounter == null)
            {
                _commitCounter = new PerformanceCounter("Memory", "Committed Bytes");
            }
            return _commitCounter.NextValue();
        }

        // --- 以下為 UI 初始化程式碼 (取代 Form Designer) ---
        private void InitializeComponent_Custom()
        {
            this.Text = "VM 記憶體極限壓力測試工具 (x64)";
            this.Size = new System.Drawing.Size(600, 500);
            
            lblTarget = new Label() { Text = "設定目標記憶體 (MB):", Location = new System.Drawing.Point(20, 20), AutoSize = true };
            txtTargetMB = new TextBox() { Text = "4096", Location = new System.Drawing.Point(160, 18), Width = 100 };
            btnStart = new Button() { Text = "開始測試", Location = new System.Drawing.Point(280, 16), Width = 80 };
            btnStop = new Button() { Text = "停止/釋放", Location = new System.Drawing.Point(370, 16), Width = 80, Enabled = false };
            
            btnStart.Click += btnStart_Click;
            btnStop.Click += btnStop_Click;

            grpStatus = new GroupBox() { Text = "即時監控狀態 (每秒更新)", Location = new System.Drawing.Point(20, 60), Size = new System.Drawing.Size(540, 150) };
            
            lblProcessMem = new Label() { Text = "1. 本程式使用: 0 MB", Location = new System.Drawing.Point(20, 30), AutoSize = true, Font = new System.Drawing.Font("微軟正黑體", 10, System.Drawing.FontStyle.Bold) };
            lblSystemPhysical = new Label() { Text = "2. 系統實體記憶體: 0 / 0 MB", Location = new System.Drawing.Point(20, 60), AutoSize = true, Font = new System.Drawing.Font("微軟正黑體", 10, System.Drawing.FontStyle.Bold) };
            lblSystemCommit = new Label() { Text = "3. SWAP 推算: 0 MB", Location = new System.Drawing.Point(20, 90), AutoSize = true, Font = new System.Drawing.Font("微軟正黑體", 10, System.Drawing.FontStyle.Bold) };

            grpStatus.Controls.Add(lblProcessMem);
            grpStatus.Controls.Add(lblSystemPhysical);
            grpStatus.Controls.Add(lblSystemCommit);

            rtbLog = new RichTextBox() { Location = new System.Drawing.Point(20, 230), Size = new System.Drawing.Size(540, 200), ReadOnly = true };

            this.Controls.Add(lblTarget);
            this.Controls.Add(txtTargetMB);
            this.Controls.Add(btnStart);
            this.Controls.Add(btnStop);
            this.Controls.Add(grpStatus);
            this.Controls.Add(rtbLog);
        }
    }
}
