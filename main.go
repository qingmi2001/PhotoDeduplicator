package main

import (
	"bufio"
	"bytes"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
)

// PhotoFile 照片元数据结构
type PhotoFile struct {
	FullPath string
	RelPath  string
	FileName string
	Size     int64
	Date     time.Time
}

var (
	// 支持的常见照片与视频格式
	validExts = map[string]bool{
		".jpg": true, ".jpeg": true, ".png": true, ".heic": true,
		".bmp": true, ".webp": true, ".tif": true, ".tiff": true,
		".raw": true, ".cr2": true, ".nef": true, ".arw": true,
		".mp4": true, ".mov": true,
	}
	exifDateRegex = regexp.MustCompile(`(\d{4}):(\d{2}):(\d{2})\s+(\d{2}):(\d{2}):(\d{2})`)
)

func main() {
	reader := bufio.NewReader(os.Stdin)
	fmt.Println("==================================================")
	fmt.Println("        Windows 照片深度去重与智能归档工具         ")
	fmt.Println("==================================================")
	fmt.Println("核心功能:")
	fmt.Println("1. 完全重复(同名同大小) -> 自动去重，按拍摄时间归档到【年/月】")
	fmt.Println("2. 同名不同大小         -> 单独归集到【人工筛选_同名不同大小】")
	fmt.Println("3. 同大小不同名         -> 单独归集到【人工筛选_同大小不同名】")
	fmt.Println("4. 安全机制             -> 采用复制模式，原照片保持原样不动")
	fmt.Println("==================================================")

	// 1. 获取输入目录
	var srcDir string
	for {
		fmt.Print("\n请输入【待整理的照片源文件夹路径】(可直接拖入文件夹): ")
		input, _ := reader.ReadString('\n')
		srcDir = cleanPath(input)
		if fi, err := os.Stat(srcDir); err == nil && fi.IsDir() {
			break
		}
		fmt.Println("❌ 路径不存在或不是文件夹，请重新输入！")
	}

	// 2. 获取输出目录
	var destDir string
	for {
		fmt.Print("请输入【整理后保存的目标文件夹路径】(可直接拖入文件夹): ")
		input, _ := reader.ReadString('\n')
		destDir = cleanPath(input)
		if destDir != "" {
			_ = os.MkdirAll(destDir, 0755)
			break
		}
		fmt.Println("❌ 路径不能为空，请重新输入！")
	}

	fmt.Println("\n正在快速扫描所有照片与层级目录，请稍候...")
	startTime := time.Now()

	// 3. 递归扫描照片
	var allFiles []*PhotoFile
	_ = filepath.Walk(srcDir, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() {
			return nil
		}
		ext := strings.ToLower(filepath.Ext(info.Name()))
		if !validExts[ext] {
			return nil
		}

		rel, _ := filepath.Rel(srcDir, path)
		shootDate := extractShootDate(path, info.ModTime())

		allFiles = append(allFiles, &PhotoFile{
			FullPath: path,
			RelPath:  rel,
			FileName: info.Name(),
			Size:     info.Size(),
			Date:     shootDate,
		})
		return nil
	})

	totalFound := len(allFiles)
	if totalFound == 0 {
		fmt.Println("⚠️ 未在源文件夹中找到任何支持的照片/视频文件。")
		waitExit(reader)
		return
	}
	fmt.Printf("扫描完成！共发现 %d 个照片/媒体文件。\n\n", totalFound)

	// 4. 构建索引
	nameSizeMap := make(map[string][]*PhotoFile) // key: lower(name)_size
	for _, f := range allFiles {
		key := strings.ToLower(f.FileName) + "_" + strconv.FormatInt(f.Size, 10)
		nameSizeMap[key] = append(nameSizeMap[key], f)
	}

	// 4.1 识别第一类：完全重复项（同名同大小）
	var activePool []*PhotoFile
	var skippedCount int
	var savedBytes int64

	for _, group := range nameSizeMap {
		activePool = append(activePool, group[0]) // 保留第一个作为代表文件
		if len(group) > 1 {
			for i := 1; i < len(group); i++ {
				skippedCount++
				savedBytes += group[i].Size
			}
		}
	}

	// 4.2 针对有效文件池，建立文件名与大小映射
	nameMap := make(map[string][]*PhotoFile)
	sizeMap := make(map[int64][]*PhotoFile)
	for _, f := range activePool {
		nameLower := strings.ToLower(f.FileName)
		nameMap[nameLower] = append(nameMap[nameLower], f)
		sizeMap[f.Size] = append(sizeMap[f.Size], f)
	}

	// 4.3 分类归集
	dirArchived := filepath.Join(destDir, "整理归档")
	dirDiffSizeSameName := filepath.Join(destDir, "人工筛选_同名不同大小")
	dirDiffNameSameSize := filepath.Join(destDir, "人工筛选_同大小不同名")

	var countArchived, countSameNameDiffSize, countSameSizeDiffName int

	fmt.Println("正在进行智能分类与年月归档...")

	for _, f := range activePool {
		nameLower := strings.ToLower(f.FileName)
		isSameNameDiffSize := len(nameMap[nameLower]) > 1
		isSameSizeDiffName := len(sizeMap[f.Size]) > 1

		if isSameNameDiffSize {
			// 第二类：同名不同大小
			subDir := filepath.Join(dirDiffSizeSameName, sanitizeDirName(f.FileName))
			_ = os.MkdirAll(subDir, 0755)
			targetName := fmt.Sprintf("[%d字节]_%s", f.Size, f.FileName)
			targetPath := filepath.Join(subDir, targetName)
			_ = copyFile(f.FullPath, targetPath)
			countSameNameDiffSize++
		} else if isSameSizeDiffName {
			// 第三类：同大小不同名
			subDir := filepath.Join(dirDiffNameSameSize, fmt.Sprintf("大小_%d字节", f.Size))
			_ = os.MkdirAll(subDir, 0755)
			targetPath := filepath.Join(subDir, f.FileName)
			_ = copyFile(f.FullPath, targetPath)
			countSameSizeDiffName++
		} else {
			// 第一类母本及无冲突独立文件：按“年/月”建立文件夹
			yearFolder := fmt.Sprintf("%d年", f.Date.Year())
			monthFolder := fmt.Sprintf("%d月", f.Date.Month())
			targetDir := filepath.Join(dirArchived, yearFolder, monthFolder)
			_ = os.MkdirAll(targetDir, 0755)

			targetPath := getUniqueTargetPath(targetDir, f.FileName)
			_ = copyFile(f.FullPath, targetPath)
			countArchived++
		}
	}

	duration := time.Since(startTime).Round(time.Millisecond)

	// 5. 生成统计日志报告
	reportPath := filepath.Join(destDir, "去重与归档统计报告.txt")
	writeReport(reportPath, totalFound, countArchived, skippedCount, countSameNameDiffSize, countSameSizeDiffName, savedBytes, duration)

	fmt.Println("\n================ 处理完成 ================")
	fmt.Printf("总扫描文件数:             %d 个\n", totalFound)
	fmt.Printf("已按拍摄年月归档保存:     %d 个 (存放在: 整理归档/)\n", countArchived)
	fmt.Printf("完全重复已自动去重:       %d 个 (共节省空间: %.2f MB)\n", skippedCount, float64(savedBytes)/(1024*1024))
	fmt.Printf("同名不同大小(待人工核对): %d 个 (存放在: 人工筛选_同名不同大小/)\n", countSameNameDiffSize)
	fmt.Printf("同大小不同名(待人工核对): %d 个 (存放在: 人工筛选_同大小不同名/)\n", countSameSizeDiffName)
	fmt.Printf("耗时:                     %s\n", duration)
	fmt.Printf("详细处理报告已保存至:     %s\n", reportPath)
	fmt.Println("==========================================")

	waitExit(reader)
}

// extractShootDate 提取照片真实拍摄日期（优先解析 Exif，降级使用文件时间）
func extractShootDate(filePath string, fallback time.Time) time.Time {
	file, err := os.Open(filePath)
	if err != nil {
		return fallback
	}
	defer file.Close()

	header := make([]byte, 64*1024) // 读取前 64KB，涵盖标准 Exif 信息
	n, _ := file.Read(header)
	if n > 0 {
		matches := exifDateRegex.FindSubmatch(header[:n])
		if len(matches) >= 7 {
			y, _ := strconv.Atoi(string(matches[1]))
			m, _ := strconv.Atoi(string(matches[2]))
			d, _ := strconv.Atoi(string(matches[3]))
			h, _ := strconv.Atoi(string(matches[4]))
			min, _ := strconv.Atoi(string(matches[5]))
			s, _ := strconv.Atoi(string(matches[6]))
			if y >= 1970 && y <= 2100 && m >= 1 && m <= 12 && d >= 1 && d <= 31 {
				return time.Date(y, time.Month(m), d, h, min, s, 0, time.Local)
			}
		}
	}
	return fallback
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()

	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	defer out.Close()

	_, err = io.Copy(out, in)
	return err
}

func getUniqueTargetPath(dir, fileName string) string {
	ext := filepath.Ext(fileName)
	base := strings.TrimSuffix(fileName, ext)
	target := filepath.Join(dir, fileName)
	counter := 1
	for {
		if _, err := os.Stat(target); os.IsNotExist(err) {
			return target
		}
		target = filepath.Join(dir, fmt.Sprintf("%s_%d%s", base, counter, ext))
		counter++
	}
}

func cleanPath(p string) string {
	p = strings.TrimSpace(p)
	p = strings.Trim(p, `"'`)
	return filepath.Clean(p)
}

func sanitizeDirName(name string) string {
	ext := filepath.Ext(name)
	base := strings.TrimSuffix(name, ext)
	invalid := []string{"\\", "/", ":", "*", "?", "\"", "<", ">", "|"}
	for _, char := range invalid {
		base = strings.ReplaceAll(base, char, "_")
	}
	return strings.TrimSpace(base)
}

func writeReport(reportPath string, total, archived, skipped, diffSize, diffName int, savedBytes int64, dur time.Duration) {
	var buf bytes.Buffer
	buf.WriteString("================ 照片去重与归档统计报告 ================\r\n")
	buf.WriteString(fmt.Sprintf("生成时间: %s\r\n", time.Now().Format("2006-01-02 15:04:05")))
	buf.WriteString(fmt.Sprintf("处理耗时: %s\r\n\r\n", dur))
	buf.WriteString(fmt.Sprintf("1. 扫描总文件数:             %d\r\n", total))
	buf.WriteString(fmt.Sprintf("2. 成功按年月归档文件数:     %d\r\n", archived))
	buf.WriteString(fmt.Sprintf("3. 完全重复已去重文件数:     %d (节省空间约 %.2f MB)\r\n", skipped, float64(savedBytes)/(1024*1024)))
	buf.WriteString(fmt.Sprintf("4. 同名不同大小(待核对)数:   %d (存放于: 人工筛选_同名不同大小/)\r\n", diffSize))
	buf.WriteString(fmt.Sprintf("5. 同大小不同名(待核对)数:   %d (存放于: 人工筛选_同大小不同名/)\r\n", diffName))
	buf.WriteString("========================================================\r\n")
	_ = os.WriteFile(reportPath, buf.Bytes(), 0644)
}

func waitExit(reader *bufio.Reader) {
	fmt.Print("\n按回车键退出程序...")
	_, _ = reader.ReadString('\n')
}
