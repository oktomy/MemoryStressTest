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
