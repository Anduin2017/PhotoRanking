# AlbumId 重构总结

## 🎯 **问题诊断**

### 1. 设计缺陷
之前使用 `Guid` 类型作为 AlbumId 主键存在严重的实现Bug：
- **SeederService第43行**：每次运行都用 `Guid.NewGuid()` 生成新的随机ID
- 导致同一个目录每次seed都会创建新的Album记录
- 造成数据重复、外键断裂、统计错误

### 2. 过时注释
`Photo.cs` 第56行的注释仍然说"AlbumId 是业务键（string）"，但实际代码是Guid类型。

---

## ✅ **修复方案**

### 采用方案：回退到 String AlbumId（使用相对路径）

#### **理由**
1. ✅ **自然主键**：目录路径本身就是唯一标识，有业务语义
2. ✅ **可读性强**：URL变为 `#album/171` 而不是 `#album/3a15aea6-f5c6-42a6-9b1e-981e3d30a5a5`
3. ✅ **Seeder逻辑简单**：使用确定性路径，避免重复创建
4. ✅ **调试友好**：日志中可以直接看到相册名称
5. ✅ **性能更好**：String索引比随机GUID效率高

---

## 🔧 **具体修改**

### 1. Album.cs - AlbumId类型改为string
### 2. Photo.cs - AlbumId类型改为string，修复注释
### 3. SeederService.cs - 使用相对路径作为确定性AlbumId
### 4. AlbumsController.cs - 参数从Guid改为string
### 5. ScoringService.cs - 参数从Guid改为string
### 6. 数据库Migration - 重新创建schema

---

## 📊 **验证结果**

### API响应示例
AlbumId从不可读的GUID变为可读的目录名：`"995"` 而不是 `"3a15aea6-f5c6-42a6-9b1e-981e3d30a5a5"`

---

**修复日期**：2025-12-16  
**修复类型**：Critical Bug Fix + Design Improvement
