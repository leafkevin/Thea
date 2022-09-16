using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Thea.Excel;

namespace Thea;

public interface IFileService
{
    TheaFileInfo Upload(string appRoot, string fileName, Stream fileStream);
    Task<TheaFileInfo> Create(string appRoot, string fileName, Func<Stream, Task> streamOperate);
    Task<TheaFileInfo> Open(string storageKey, string fileId, FileMode fileMode, FileAccess fileAccess, Func<Stream, Task> streamOperate);
    Task<TheaFileInfo> Replace(string storageKey, string fileId, Stream fileStream, int updatedBy);
    Task<TheaFileInfo> Replace(string storageKey, string fileId, string fileName, Stream fileStream, int updatedBy);
    Task<TheaFileInfo> Save(string storageKey, TheaFileInfo fileInfo, string fullPathWithoutFileName, string sourceType, string sourceId);
    Task<TheaFileInfo> Save(string storageKey, TheaFileInfo fileInfo, string fullPathWithoutFileName, int customerId, string sourceType, string sourceId, int updatedBy);
    Task<List<TheaFileInfo>> BatchSave(string storageKey, List<TheaFileInfo> fileInfos, string fullPathWithoutFileName, string sourceType, string sourceId);
    Task<List<TheaFileInfo>> BatchSave(string storageKey, List<TheaFileInfo> fileInfos, string fullPathWithoutFileName, int customerId, string sourceType, string sourceId, int updatedBy);
    Task<List<TheaFileInfo>> GetFiles(string storageKey, string fileKey);
    Task<TheaFileInfo> GetFile(string storageKey, string fileId);
    Task DeleteFile(string storageKey, string fileId);
    Task DeleteFiles(string storageKey, string fileKey);
    TheaFileInfo GenerateTemporaryFileInfo(string appRoot, string fileName);
    IPagedList<TEntity> ToFileUrl<TEntity>(string storageKey, IPagedList<TEntity> entities, string fileIdField, string fileUrlField);
    List<TEntity> ToFileUrl<TEntity>(string storageKey, List<TEntity> entities, string fileIdField, string fileUrlField);
    IPagedList<TEntity> ToFileInfo<TEntity>(string storageKey, IPagedList<TEntity> entities, string fileIdField, string fileInfoField);
    List<TEntity> ToFileInfo<TEntity>(string storageKey, List<TEntity> entities, string fileIdField, string fileInfoField);
    Task<TheaFileInfo> Export<T>(string appRoot, string fileName, List<T> exportData, Action<ExcelExportInfoBuilder<T>> initializer);
    Task<TheaFileInfo> Export<T>(string appRoot, string fileName, List<T> exportData, ExcelExportInfo exportInfo);
    Task Export<T>(Stream outputStream, string fileName, List<T> exportData, Action<ExcelExportInfoBuilder<T>> initializer);
    Task Export<T>(Stream outputStream, string fileName, List<T> exportData, ExcelExportInfo exportInfo);
}
