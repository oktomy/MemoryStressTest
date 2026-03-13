How to Verify Results (Interpretation Instructions)

After compiling and running the tool, set it to 8192 MB (8GB) or higher, and observe the values ​​on the interface:

This program uses physical memory (Item 1):

This value should steadily increase over time.

Normal situation: If the VM supports it, this value should be close to 14GB (on a 16GB VM, the OS itself occupies approximately 2GB).

Abnormal situation: If this value gets stuck at 4GB, but "SWAP/PageFile" starts to spike, it means the VM's Hypervisor is limiting the supply of physical RAM (the Ballooning driver is reclaiming memory), or Windows has set a very low Working Set limit.

System SWAP/PageFile Calculation (Item 3):

Definition: This is Committed Bytes (the total memory committed by the OS to all programs) minus Total Physical RAM.

When this value is > 0 and continues to increase, it indicates that the physical memory has been exhausted and the system is using the hard drive (PageFile) as memory. At this point, the program will slow down significantly.

Log File:

The program will generate MemTest_Log.csv in the same directory.

You can open this with Excel to create charts. If you see Process_WorkingSet stop increasing, but System_Estimated_Swap starts increasing, that crossover point is the true "available physical memory performance bottleneck" for that VM.

如何驗證結果 (判讀說明)
當您編譯並執行工具後，設定為 8192 MB (8G) 或更高，請觀察介面上的數值：

本程式使用實體記憶體 (Item 1)：

這個數值應該要隨著時間穩定上升。

正常情況：如果 VM 支援，這個數值應該能接近 14GB (在 16GB 的 VM 上，OS 本身約佔 2G)。

異常情況：如果這個數值卡在 4GB 上不去，但 "SWAP/PageFile" 開始狂飆，代表 VM 的 Hypervisor 限制了實體 RAM 的供給 (Ballooning driver 正在回收記憶體)，或者 Windows 設定了極低的 Working Set 上限。

系統 SWAP/PageFile 推算 (Item 3)：

定義：這是 Committed Bytes (OS 承諾給所有程式的記憶體總和) 減去 Total Physical RAM。

當此數值 > 0 且持續變大，表示實體記憶體已經用光，系統正在使用硬碟 (PageFile) 當作記憶體。這時候程式會顯著變慢。

Log 檔案：

程式會在同目錄下產生 MemTest_Log.csv。

您可以用 Excel 打開，製作圖表。如果看到 Process_WorkingSet 停止上升，但 System_Estimated_Swap 開始上升，那個交叉點就是該 VM 真正的「可用實體記憶體效能瓶頸點」。


