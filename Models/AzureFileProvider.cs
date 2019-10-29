using System;
using System.Collections.Generic;
using System.Linq;
using Syncfusion.EJ2.FileManager.Base;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Net;
using Newtonsoft.Json.Serialization;
using System.Web;
using System.Web.Mvc;


namespace Syncfusion.EJ2.FileManager.AzureFileProvider
{
    public class AzureFileProvider : AzureFileProviderBase
    {
        List<FileManagerDirectoryContent> Items = new List<FileManagerDirectoryContent>();
        private CloudBlobContainer container;
        private string PathValue;
        private string BlobPath;
        private string FilesPath;
        private long size;
        private string rootPath;
        private List<string> existFiles = new List<string>();
        private List<string> missingFiles = new List<string>();
        private bool isFolderAvailable = false;
        private List<FileManagerDirectoryContent> copiedFiles = new List<FileManagerDirectoryContent>();
        private CloudBlobDirectory item;

        // Registering the azure storage 
        public void RegisterAzure(string accountName, string accountKey, string blobName)
        {
            container = new CloudStorageAccount(new StorageCredentials(accountName, accountKey), useHttps: true).CreateCloudBlobClient().GetContainerReference(blobName);
        }
        // Sets blob and file path
        public void setBlobContainer(string blobPath, string filePath)
        {
            BlobPath = blobPath;
            FilesPath = filePath;
            rootPath = FilesPath.Replace(BlobPath, "");
        }

        // Performs files operations
        protected async Task<BlobResultSegment> AsyncReadCall(string path, string oper)
        {
            CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(path);
            BlobRequestOptions options = new BlobRequestOptions();
            OperationContext context = new OperationContext();
            dynamic Asyncitem = null;
            if (oper == "Read") Asyncitem = await sampleDirectory.ListBlobsSegmentedAsync(false, BlobListingDetails.Metadata, null, null, options, context);
            if (oper == "Paste") Asyncitem = await sampleDirectory.ListBlobsSegmentedAsync(false, BlobListingDetails.None, null, null, options, context);
            if (oper == "Rename") Asyncitem = await sampleDirectory.ListBlobsSegmentedAsync(true, BlobListingDetails.Metadata, null, null, options, context);
            if (oper == "Remove") Asyncitem = await sampleDirectory.ListBlobsSegmentedAsync(true, BlobListingDetails.None, null, null, options, context);
            if (oper == "HasChild") Asyncitem = await sampleDirectory.ListBlobsSegmentedAsync(false, BlobListingDetails.None, null, null, options, context);
            //return Asyncitem;
            return await Task.Run(() =>
            {
                return Asyncitem;
            });
        }
        // Reads the storage 
        public FileManagerResponse GetFiles(string path, FileManagerDirectoryContent[] selectedItems)
        {
            var filter = "*.*";
            FileManagerResponse readResponse = new FileManagerResponse();
            List<FileManagerDirectoryContent> details = new List<FileManagerDirectoryContent>();
            FileManagerDirectoryContent cwd = new FileManagerDirectoryContent();
            try
            {
                string[] extensions = ((filter.Replace(" ", "")) ?? "*").Split(",|;".ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries);
                CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(path);
                cwd.Name = sampleDirectory.Prefix.Split(new string[] { sampleDirectory.Parent.Prefix }, StringSplitOptions.None)[sampleDirectory.Prefix.Split(new string[] { sampleDirectory.Parent.Prefix }, StringSplitOptions.None).Length - 1].Replace("/", "");
                cwd.Type = "File Folder";
                cwd.FilterPath = selectedItems.Length > 0 ? selectedItems[0].FilterPath : "";
                cwd.Size = 0;
                cwd.HasChild = HasChildDirectory(path);
                readResponse.CWD = cwd;
                BlobResultSegment items = Task.Run(() => AsyncReadCall(path, "Read")).Result;
                foreach (IListBlobItem item in items.Results)
                {
                    bool includeItem = true;
                    if (!(extensions[0].Equals("*.*") || extensions[0].Equals("*")) && item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob file = (CloudBlockBlob)item;
                        if (!(Array.IndexOf(extensions, "*." + (file.Name.ToString().Trim().Split('.'))[(file.Name.ToString().Trim().Split('.')).Count() - 1]) >= 0))
                            includeItem = false;
                    }
                    if (includeItem)
                    {
                        FileManagerDirectoryContent entry = new FileManagerDirectoryContent();
                        if (item.GetType() == typeof(CloudBlockBlob))
                        {
                            CloudBlockBlob file = (CloudBlockBlob)item;
                            entry.Name = file.Name.Replace(path, "");
                            entry.Type = Path.GetExtension(file.Name.Replace(path, ""));
                            entry.IsFile = true;
                            entry.Size = file.Properties.Length;
                            entry.DateModified = file.Properties.LastModified.Value.LocalDateTime;
                            entry.HasChild = false;
                            entry.FilterPath = selectedItems.Length > 0 ? path.Replace(rootPath, "") : "/";
                            details.Add(entry);
                        }
                        else if (item.GetType() == typeof(CloudBlobDirectory))
                        {
                            CloudBlobDirectory directory = (CloudBlobDirectory)item;
                            entry.Name = directory.Prefix.Replace(path, "").Replace("/", "");
                            entry.Type = "Directory";
                            entry.IsFile = false;
                            entry.Size = 0;
                            entry.HasChild = HasChildDirectory(directory.Prefix);
                            entry.FilterPath = selectedItems.Length > 0 ? path.Replace(this.rootPath, "") : "/";
                            details.Add(entry);
                        }
                    }
                }
            }
            catch (Exception) { return readResponse; }
            readResponse.Files = details;
            return readResponse;
        }
        // Converts the byte size value to appropriate value
        protected string byteConversion(long fileSize)
        {
            try
            {
                string[] index = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
                if (fileSize == 0) return "0 " + index[0];
                int value = Convert.ToInt32(Math.Floor(Math.Log(Math.Abs(fileSize), 1024)));
                return (Math.Sign(fileSize) * Math.Round(Math.Abs(fileSize) / Math.Pow(1024, value), 1)).ToString() + " " + index[value];
            }
            catch (Exception ex) { throw ex; }
        }
        // Gets the SizeValue of the Directory
        protected void getSizeValue(string path)
        {
            BlobResultSegment items = Task.Run(() => AsyncReadCall(path, "Read")).Result;
            foreach (IListBlobItem item in items.Results)
            {
                if (item is CloudBlockBlob blockBlob)
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference(new CloudBlockBlob(item.Uri).Name);
                    blob.FetchAttributes();
                    size = size + blob.Properties.Length;
                }
                else if (item is CloudBlobDirectory blobDirectory)
                    getSizeValue(item.Uri.ToString().Replace(BlobPath, "")); // set your download target path as below methods parameter
            }
        }
        // Gets Details of the files
        public FileManagerResponse Details(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => GetDetailsAsync(path, names, data)).Result;
        }
        // Gets the details
        protected FileManagerResponse GetDetailsAsync(string path, string[] names, IEnumerable<object> selectedItems = null)
        {
            bool isVariousFolders = false;
            string previousPath = "";
            string previousName = "";
            FileManagerResponse detailsResponse = new FileManagerResponse();
            try
            {
                bool isFile = false;
                bool namesAvailable = names.Length > 0 ? true : false;
                if (names.Length == 0 && selectedItems != null)
                {
                    List<string> values = new List<string>();
                    foreach (FileManagerDirectoryContent item in selectedItems) { values.Add(item.Name); }
                    names = values.ToArray();
                }
                FileDetails fileDetails = new FileDetails();
                long multipleSize = 0;
                if (selectedItems != null)
                {
                    foreach (FileManagerDirectoryContent fileItem in selectedItems)
                    {
                        if (names.Length == 1)
                        {
                            if (fileItem.IsFile)
                            {
                                var blob = container.GetBlockBlobReference(rootPath + fileItem.FilterPath + fileItem.Name);
                                isFile = fileItem.IsFile;
                                fileDetails.IsFile = isFile;
                                blob.FetchAttributes();
                                fileDetails.Name = fileItem.Name;
                                fileDetails.Location = ((namesAvailable ? (rootPath + fileItem.FilterPath + fileItem.Name) : path)).Replace("/", @"\");
                                fileDetails.Size = byteConversion(blob.Properties.Length);
                                fileDetails.Modified = blob.Properties.LastModified.Value.LocalDateTime;
                                detailsResponse.Details = fileDetails;
                            }
                            else
                            {
                                CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(rootPath + fileItem.FilterPath + fileItem.Name);
                                getSizeValue((namesAvailable ? rootPath + fileItem.FilterPath + fileItem.Name : path));
                                long sizeValue = size;
                                isFile = false;
                                fileDetails.Name = fileItem.Name;
                                fileDetails.Location = ((namesAvailable ? rootPath + fileItem.FilterPath + fileItem.Name : path.Substring(0, path.Length - 1))).Replace("/", @"\");
                                fileDetails.Size = byteConversion(sizeValue);
                                fileDetails.Modified = fileItem.DateModified;
                                detailsResponse.Details = fileDetails;
                            }
                        }
                        else
                        {
                            if (fileItem.IsFile) getSizeValue((namesAvailable ? rootPath + fileItem.FilterPath + fileItem.Name : path));
                            multipleSize = multipleSize + (fileItem.IsFile ? fileItem.Size : size);
                            size = 0;
                            fileDetails.Name = previousName = previousName + fileItem.Name + ",";
                            previousPath = previousPath == "" ? rootPath + fileItem.FilterPath : previousPath;
                            if (previousPath == rootPath + fileItem.FilterPath && !isVariousFolders)
                            {
                                previousPath = rootPath + fileItem.FilterPath;
                                fileDetails.Location = ((rootPath + fileItem.FilterPath).Replace("/", @"\")).Substring(0, ((rootPath + fileItem.FilterPath).Replace("/", @"\")).Length - 1);
                            }
                            else
                            {
                                isVariousFolders = true;
                                fileDetails.Location = "Various Folders";
                            }
                            fileDetails.Size = byteConversion(multipleSize);
                            fileDetails.MultipleFiles = true;
                            detailsResponse.Details = fileDetails;
                        }

                    }
                }
                size = 0;
                detailsResponse.Details.Name = detailsResponse.Details.Name.TrimEnd(',');
                return detailsResponse;
            }
            catch (Exception ex) { throw ex; }
        }
        // Creates a NewFolder
        public FileManagerResponse Create(string path, string name, params FileManagerDirectoryContent[] selectedItems)
        {
            isFolderAvailable = false;
            Task.Run(() => CreateFolderAsync(path, name, selectedItems)).GetAwaiter().GetResult();
            FileManagerResponse createResponse = new FileManagerResponse();
            if (!this.isFolderAvailable)
            {
                FileManagerDirectoryContent content = new FileManagerDirectoryContent();
                content.Name = name;
                FileManagerDirectoryContent[] directories = new[] { content };
                createResponse.Files = (IEnumerable<FileManagerDirectoryContent>)directories;
            }
            else
            {
                ErrorDetails er = new ErrorDetails();
                er.FileExists = existFiles;
                er.Code = "400";
                er.Message = "Folder Already Already Exists";
                createResponse.Error = er;
            }
            return createResponse;
        }
        // Creates a NewFolder
        protected async Task CreateFolderAsync(string path, string name, IEnumerable<object> selectedItems = null)
        {
            BlobResultSegment items = Task.Run(() => AsyncReadCall(path, "Read")).Result;
            string checkName = name.Contains(" ") ? name.Replace(" ", "%20") : name;
            if (await IsFolderExists(path + name) || (items.Results.Where(x => x.Uri.Segments.Last().Replace("/", "").ToLower() == checkName.ToLower()).Select(i => i).ToArray().Length > 0))
                this.isFolderAvailable = true;
            else
            {
                CloudBlockBlob blob = container.GetBlockBlobReference(path + name + "/About.txt");
                blob.Properties.ContentType = "text/plain";
                await blob.UploadTextAsync("This is a auto generated file");
            }
        }
        // Renames file(s) or folder(s)
        public FileManagerResponse Rename(string path, string oldName, string newName, bool replace = false, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => RenameAsync(path, oldName, newName, data)).Result;
        }
        // Renames file(s) or folder(s)
        protected async Task<FileManagerResponse> RenameAsync(string path, string oldName, string newName, params FileManagerDirectoryContent[] selectedItems)
        {
            FileManagerResponse renameResponse = new FileManagerResponse();
            List<FileManagerDirectoryContent> details = new List<FileManagerDirectoryContent>();
            FileManagerDirectoryContent entry = new FileManagerDirectoryContent();
            bool isAlreadyAvailable = false;
            bool isFile = false;
            foreach (FileManagerDirectoryContent FileItem in selectedItems)
            {
                FileManagerDirectoryContent s_item = FileItem;
                isFile = s_item.IsFile;
                isAlreadyAvailable = (isFile) ? await IsFileExists(path + newName) : await IsFolderExists(path + newName);
                entry.Name = newName;
                entry.Type = s_item.Type;
                entry.IsFile = isFile;
                entry.Size = s_item.Size;
                entry.HasChild = s_item.HasChild;
                entry.FilterPath = path;
                details.Add(entry);
                break;
            }
            BlobResultSegment files = Task.Run(() => AsyncReadCall(path, "Read")).Result;
            string checkName = newName.Contains(" ") ? newName.Replace(" ", "%20") : newName;
            if ((files.Results.Where(x => x.Uri.Segments.Last().Replace("/", "").ToLower() == checkName.ToLower()).Select(i => i).ToArray().Length > 0))
                isAlreadyAvailable = true;
            if (!isAlreadyAvailable)
            {
                if (isFile)
                {
                    CloudBlob existBlob = container.GetBlobReference(path + oldName);
                    await (container.GetBlobReference(path + newName)).StartCopyAsync(existBlob.Uri);
                    await existBlob.DeleteIfExistsAsync();
                }
                else
                {
                    CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(path + oldName);
                    BlobResultSegment items = Task.Run(() => AsyncReadCall(path + oldName, "Rename")).Result;
                    foreach (IListBlobItem item in items.Results)
                    {
                        string name = item.Uri.AbsolutePath.Replace(sampleDirectory.Uri.AbsolutePath, "").Replace("%20", " ");
                        await (container.GetBlobReference(path + newName + "/" + name)).StartCopyAsync(item.Uri);
                        await container.GetBlobReference(path + oldName + "/" + name).DeleteAsync();
                    }

                }
                renameResponse.Files = details;
            }
            else
            {
                ErrorDetails er = new ErrorDetails();
                er.FileExists = existFiles;
                er.Code = "400";
                er.Message = "File or Folder Already Already Exists";
                renameResponse.Error = er;
            }
            return renameResponse;
        }
        // Deletes file(s) or folder(s)
        public FileManagerResponse Delete(string path, string[] names, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => RemoveAsync(names, path, data)).Result;
        }
        // Deletes file(s) or folder(s)
        protected async Task<FileManagerResponse> RemoveAsync(string[] names, string path, params FileManagerDirectoryContent[] selectedItems)
        {
            FileManagerResponse removeResponse = new FileManagerResponse();
            List<FileManagerDirectoryContent> details = new List<FileManagerDirectoryContent>();
            CloudBlobDirectory directory = item;
            FileManagerDirectoryContent entry = new FileManagerDirectoryContent();
            CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(path);
            foreach (FileManagerDirectoryContent FileItem in selectedItems)
            {
                if (FileItem.IsFile)
                {
                    path = this.FilesPath.Replace(this.BlobPath, "") + FileItem.FilterPath;
                    CloudBlockBlob blockBlob = container.GetBlockBlobReference(path + FileItem.Name);
                    await blockBlob.DeleteAsync();
                    entry.Name = FileItem.Name;
                    entry.Type = FileItem.Type;
                    entry.IsFile = FileItem.IsFile;
                    entry.Size = FileItem.Size;
                    entry.HasChild = FileItem.HasChild;
                    entry.FilterPath = path;
                    details.Add(entry);
                }
                else
                {
                    path = FilesPath.Replace(BlobPath, "") + FileItem.FilterPath;
                    CloudBlobDirectory subDirectory = container.GetDirectoryReference(path + FileItem.Name);
                    BlobResultSegment items = Task.Run(() => AsyncReadCall(path + FileItem.Name, "Remove")).Result;
                    foreach (IListBlobItem item in items.Results)
                    {
                        CloudBlockBlob blockBlob = container.GetBlockBlobReference(path + FileItem.Name + "/" + item.Uri.ToString().Replace(subDirectory.Uri.ToString(), ""));
                        await blockBlob.DeleteAsync();
                        entry.Name = FileItem.Name;
                        entry.Type = FileItem.Type;
                        entry.IsFile = FileItem.IsFile;
                        entry.Size = FileItem.Size;
                        entry.HasChild = FileItem.HasChild;
                        entry.FilterPath = path;
                        details.Add(entry);
                    }
                }
            }
            removeResponse.Files = details;
            return removeResponse;
        }


        // Upload file(s) to the storage
        public FileManagerResponse Upload(string path, IList<HttpPostedFileBase> files, string action, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => UploadAsync(files, path, action, data)).Result;
        }
        // Upload file(s) to the storage
        protected async Task<FileManagerResponse> UploadAsync(IList<HttpPostedFileBase> files, string path, string action, params FileManagerDirectoryContent[] selectedItems)
        {
            FileManagerResponse uploadResponse = new FileManagerResponse();
            try
            {
                string[] v = new string[] { files[0].FileName };
                if (await IsFileExists(path + files[0].FileName) && action == "save")
                {
                    ErrorDetails er = new ErrorDetails();
                    er.Code = "400";
                    er.Message = "File already exists.";
                    er.FileExists = v;
                    uploadResponse.Error = er;
                }
                else
                {
                    if (action == "replace")
                    {
                        if (selectedItems is null)
                        {
                            if (await IsFileExists(path + files[0].FileName))
                            {
                                CloudBlob newBlob = container.GetBlobReference(path + files[0].FileName);
                                await newBlob.DeleteIfExistsAsync();
                            }
                            if (await IsFolderExists(path + files[0].FileName))
                            {
                                CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(path + files[0].FileName);
                                BlobResultSegment items = Task.Run(() => AsyncReadCall(path + files[0].FileName, "Read")).Result;
                                foreach (IListBlobItem item in items.Results)
                                {
                                    string name = item.Uri.AbsolutePath.Replace(sampleDirectory.Uri.AbsolutePath, "").Replace("%20", " ");
                                    await container.GetBlobReference(path + files[0].FileName + "/" + name).DeleteAsync();
                                }
                            }
                        }
                    }
                    string newName = "";
                    if (action == "keepboth")
                    {
                        newName = await FileRename(path, files[0].FileName);
                    }
                    CloudBlockBlob blockBlob = container.GetBlockBlobReference(path.Replace(BlobPath, "") + (String.IsNullOrEmpty(newName) ? files[0].FileName : newName));
                    blockBlob.Properties.ContentType = files[0].ContentType;
                    files[0].InputStream.Position = 0;
                    await blockBlob.UploadFromStreamAsync(files[0].InputStream);
                }
            }
            catch (Exception ex) { throw ex; }
            return uploadResponse;
        }
        protected async Task CopyFileToTemp(string path, CloudBlockBlob blockBlob)
        {
            using (FileStream fileStream = File.Create(path))
            {
                await blockBlob.DownloadToStreamAsync(fileStream);
                fileStream.Close();
            }
        }
        // Download file(s) from the storage
        public virtual FileStreamResult Download(string path, string[] names = null, params FileManagerDirectoryContent[] selectedItems)
        {
            return Task.Run(() => DownloadAsync(this.FilesPath + path + "", names, selectedItems)).GetAwaiter().GetResult();
        }
        // Download file(s) from the storage
        protected async Task<FileStreamResult> DownloadAsync(string path, string[] names = null, params FileManagerDirectoryContent[] selectedItems)
        {

            foreach (FileManagerDirectoryContent file in selectedItems)
            {
                if (file.IsFile && selectedItems.Count() == 1)
                {
                    FileStreamResult fileStreamResult = new FileStreamResult(new MemoryStream(new WebClient().DownloadData(FilesPath + (names[0].Contains('/') ? "/" : selectedItems[0].FilterPath) + names[0])), "APPLICATION/octet-stream");
                    fileStreamResult.FileDownloadName = file.Name;
                    return fileStreamResult;
                }
                else
                {
                    ZipArchiveEntry zipEntry = null;
                    ZipArchive archive;
                    string tempPath = Path.Combine(Path.GetTempPath(), "temp.zip");
                    using (archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
                    {
                        foreach (FileManagerDirectoryContent files in selectedItems)
                        {
                            if (string.IsNullOrEmpty(files.FilterPath))
                            {
                                files.FilterPath = "/";
                                files.Name = "";
                            }
                            string MyPath = this.FilesPath + files.FilterPath;
                            MyPath = MyPath.Replace(this.BlobPath, "");
                            if (files.IsFile)
                            {
                                CloudBlockBlob blockBlob = container.GetBlockBlobReference(MyPath + files.Name);
                                if (File.Exists(Path.Combine(Path.GetTempPath(), files.Name)))
                                    File.Delete(Path.Combine(Path.GetTempPath(), files.Name));
                                string localPath = Path.GetTempPath() + files.Name;
                                await CopyFileToTemp(localPath, blockBlob);
                                zipEntry = archive.CreateEntryFromFile(Path.GetTempPath() + files.Name, files.Name, CompressionLevel.Fastest);
                                if (File.Exists(Path.Combine(Path.GetTempPath(), files.Name)))
                                    File.Delete(Path.Combine(Path.GetTempPath(), files.Name));
                            }
                            else
                            {
                                string subFolder = MyPath.Replace(FilesPath + "/", "");
                                MyPath = MyPath.Replace(BlobPath, "");
                                PathValue = MyPath + files.Name;
                                await DownloadFolder(MyPath, subFolder + files.Name, zipEntry, archive);
                            }
                        }
                    }
                    archive.Dispose();
                    FileStream fileStreamInput = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    FileStreamResult fileStreamResult = new FileStreamResult(fileStreamInput, "APPLICATION/octet-stream");
                    fileStreamResult.FileDownloadName = "Files.zip";
                    if (File.Exists(Path.Combine(Path.GetTempPath(), "temp.zip")))
                        File.Delete(Path.Combine(Path.GetTempPath(), "temp.zip"));
                    return fileStreamResult;
                }
            }
            return null;
        }

        // Download folder(s) from the storage
        private async Task DownloadFolder(string path, string Name, ZipArchiveEntry zipEntry, ZipArchive archive)
        {
            CloudBlobDirectory sampleDirectory = container.GetDirectoryReference(PathValue);
            BlobResultSegment items = Task.Run(() => AsyncReadCall(PathValue, "Read")).Result;
            foreach (IListBlobItem item in items.Results)
            {
                if (item is CloudBlockBlob blockBlob)
                {
                    CloudBlockBlob blob = container.GetBlockBlobReference(new CloudBlockBlob(item.Uri).Name);
                    await blob.FetchAttributesAsync();
                    string localPath = Path.GetTempPath() + blob.Name.Split('/').Last();
                    if (File.Exists(localPath)) File.Delete(localPath);
                    await CopyFileToTemp(localPath, blob);
                    zipEntry = archive.CreateEntryFromFile(localPath, (Name.Contains(rootPath) ? Name.Replace(rootPath + "/", "") : Name).Replace("/", "\\") + "\\" + blob.Name.Split('/').Last(), CompressionLevel.Fastest);
                    if (File.Exists(localPath)) File.Delete(localPath);
                }
                else if (item is CloudBlobDirectory blobDirectory)
                {
                    string localPath = item.Uri.ToString().Replace(BlobPath, ""); // <-- Change your download target path here
                    PathValue = localPath;
                    string toPath = item.Uri.ToString().Replace(FilesPath + "/", "");
                    await DownloadFolder(localPath, toPath.Substring(0, toPath.Length - 1), zipEntry, archive);
                }
            }
        }
        // Check whether the directory has child
        private bool HasChildDirectory(string path)
        {
            BlobResultSegment items = Task.Run(() => AsyncReadCall(path, "HasChild")).Result;
            foreach (IListBlobItem item in items.Results)
            {
                if (item.GetType() == typeof(CloudBlobDirectory)) { return true; }
            }
            return false;
        }

        // To get the file details
        private FileManagerDirectoryContent GetFileDetails(string targetPath, FileManagerDirectoryContent fileDetails, string newName)
        {
            FileManagerDirectoryContent entry = new FileManagerDirectoryContent();
            entry.Name = String.IsNullOrEmpty(newName) ? fileDetails.Name : newName;
            entry.Type = fileDetails.Type;
            entry.IsFile = fileDetails.IsFile;
            entry.Size = fileDetails.Size;
            entry.HasChild = fileDetails.HasChild;
            entry.FilterPath = targetPath;
            return entry;
        }

        // To check if folder exists
        private async Task<bool> IsFolderExists(string path)
        {
            BlobResultSegment items = Task.Run(() => AsyncReadCall(path, "Paste")).Result;
            return await Task.Run(() => { return items.Results.Count() > 0; });
        }

        // To check if file exists
        private async Task<bool> IsFileExists(string path)
        {
            CloudBlob newBlob = container.GetBlobReference(path);
            return await Task.Run(() => newBlob.ExistsAsync());
        }

        // Copies file(s) or folders
        public FileManagerResponse Copy(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => CopyToAsync(path, targetPath, names, renameFiles, data)).GetAwaiter().GetResult();
        }

        private async Task<FileManagerResponse> CopyToAsync(string path, string targetPath, string[] names, string[] renamedFiles = null, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse copyResponse = new FileManagerResponse();
            try
            {
                renamedFiles = renamedFiles ?? new string[0];
                foreach (FileManagerDirectoryContent item in data)
                {
                    if (item.IsFile)
                    {
                        if (await IsFileExists(targetPath + item.Name).ConfigureAwait(false))
                        {
                            int index = -1;
                            if (renamedFiles.Length > 0)
                                index = Array.FindIndex(renamedFiles, Items => Items.Contains(item.Name));
                            if ((path == targetPath) || (index != -1))
                            {
                                string newName = await FileRename(targetPath, item.Name);
                                await CopyItems(rootPath + item.FilterPath, targetPath, item.Name, newName).ConfigureAwait(false);
                                copiedFiles.Add(GetFileDetails(targetPath, item, newName));
                            }
                            else this.existFiles.Add(item.Name);
                        }
                        else
                        {
                            await CopyItems(rootPath + item.FilterPath, targetPath, item.Name, null).ConfigureAwait(false);
                            copiedFiles.Add(GetFileDetails(targetPath, item, null));
                        }
                    }
                    else
                    {
                        if (!await IsFolderExists((rootPath + item.FilterPath + item.Name)).ConfigureAwait(false))
                            missingFiles.Add(item.Name);
                        else if (await IsFolderExists(targetPath + item.Name).ConfigureAwait(false))
                        {
                            int index = -1;
                            if (renamedFiles.Length > 0)
                                index = Array.FindIndex(renamedFiles, Items => Items.Contains(item.Name));
                            if ((path == targetPath) || (index != -1))
                            {
                                item.Path = rootPath + item.FilterPath + item.Name;
                                item.Name = await FileRename(targetPath, item.Name).ConfigureAwait(false);
                                CopySubFolder(item, targetPath);
                                copiedFiles.Add(GetFileDetails(targetPath, item, null));
                            }
                            else existFiles.Add(item.Name);
                        }
                        else
                        {
                            item.Path = rootPath + item.FilterPath + item.Name;
                            CopySubFolder(item, targetPath);
                            copiedFiles.Add(GetFileDetails(targetPath, item, null));
                        }
                    }

                }
                copyResponse.Files = copiedFiles;
                if (existFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    copyResponse.Error = er;
                }
                if (missingFiles.Count > 0)
                {
                    string nameList = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        nameList = nameList + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(nameList + " not found in given location.");
                }
                await Task.Delay(3800);
                return copyResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Code = "404";
                er.Message = e.Message.ToString();
                er.FileExists = copyResponse.Error?.FileExists;
                copyResponse.Error = er;
                return copyResponse;
            }
        }

        // To iterate and copy subfolder
        private async void CopySubFolder(FileManagerDirectoryContent subfolder, string targetPath)
        {
            CloudBlobDirectory blobDirectory = container.GetDirectoryReference(targetPath);
            BlobResultSegment items = Task.Run(() => AsyncReadCall(subfolder.Path, "Paste")).GetAwaiter().GetResult();
            await CreateFolderAsync(targetPath, subfolder.Name).ConfigureAwait(false);
            targetPath = targetPath + subfolder.Name + "/";
            foreach (IListBlobItem item in items.Results)
            {
                if (item.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob BlobItem = (CloudBlockBlob)item;
                    string name = BlobItem.Name.Replace(subfolder.Path + "/", "");
                    string sourcePath = BlobItem.Name.Replace(name, "");
                    await CopyItems(sourcePath, targetPath, name, null).ConfigureAwait(false);
                }
                if (item.GetType() == typeof(CloudBlobDirectory))
                {
                    CloudBlobDirectory BlobItem = (CloudBlobDirectory)item;
                    FileManagerDirectoryContent itemDetail = new FileManagerDirectoryContent();
                    itemDetail.Name = BlobItem.Prefix.Replace(subfolder.Path, "").Replace("/", "");
                    itemDetail.Path = subfolder.Path + "/" + itemDetail.Name;
                    CopySubFolder(itemDetail, targetPath);
                }
            }
        }

        // To iterate and copy files
        private async Task CopyItems(string sourcePath, string targetPath, string name, string newName)
        {
            if (newName == null) { newName = name; }
            CloudBlob existBlob = container.GetBlobReference(sourcePath + name);
            CloudBlob newBlob = container.GetBlobReference(targetPath + newName);
            await newBlob.StartCopyAsync(existBlob.Uri).ConfigureAwait(false);
        }

        // To rename files incase of duplicates
        private async Task<string> FileRename(string newPath, string fileName)
        {
            int index = fileName.LastIndexOf(".");
            string nameNotExist = string.Empty;
            nameNotExist = index >= 0 ? fileName.Substring(0, index) : fileName;
            int fileCount = 0;
            while (index > -1 ? await IsFileExists(newPath + nameNotExist + (fileCount > 0 ? "(" + fileCount.ToString() + ")" + Path.GetExtension(fileName) : Path.GetExtension(fileName))) : await IsFolderExists(newPath + nameNotExist + (fileCount > 0 ? "(" + fileCount.ToString() + ")" + Path.GetExtension(fileName) : Path.GetExtension(fileName))))
            {
                fileCount++;
            }
            fileName = nameNotExist + (fileCount > 0 ? "(" + fileCount.ToString() + ")" : "") + Path.GetExtension(fileName);
            return await Task.Run(() =>
            {
                return fileName;
            });
        }

        // Returns the image 
        public FileStreamResult GetImage(string path, string id, bool allowCompress, ImageSize size, params FileManagerDirectoryContent[] data)
        {
            return new FileStreamResult((new MemoryStream(new WebClient().DownloadData(this.FilesPath + path))), "APPLICATION/octet-stream");
        }

        private async Task MoveItems(string sourcePath, string targetPath, string name, string newName)
        {
            CloudBlob existBlob = container.GetBlobReference(sourcePath + name);
            await CopyItems(sourcePath, targetPath, name, newName).ConfigureAwait(false);
            await existBlob.DeleteIfExistsAsync().ConfigureAwait(false);
        }


        private async void MoveSubFolder(FileManagerDirectoryContent subfolder, string targetPath)
        {
            CloudBlobDirectory blobDirectory = container.GetDirectoryReference(targetPath);
            BlobResultSegment items = Task.Run(() => AsyncReadCall(subfolder.Path, "Paste")).Result;
            await CreateFolderAsync(targetPath, subfolder.Name);
            targetPath = targetPath + subfolder.Name + "/";
            foreach (IListBlobItem item in items.Results)
            {
                if (item.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob BlobItem = (CloudBlockBlob)item;
                    string name = BlobItem.Name.Replace(subfolder.Path + "/", "");
                    string sourcePath = BlobItem.Name.Replace(name, "");
                    await MoveItems(sourcePath, targetPath, name, null).ConfigureAwait(false);
                }
                if (item.GetType() == typeof(CloudBlobDirectory))
                {
                    CloudBlobDirectory BlobItem = (CloudBlobDirectory)item;
                    FileManagerDirectoryContent itemDetail = new FileManagerDirectoryContent();
                    itemDetail.Name = BlobItem.Prefix.Replace(subfolder.Path, "").Replace("/", "");
                    itemDetail.Path = subfolder.Path + "/" + itemDetail.Name;
                    CopySubFolder(itemDetail, targetPath);
                }
            }
        }

        // Moves file(s) or folders
        public FileManagerResponse Move(string path, string targetPath, string[] names, string[] renameFiles, FileManagerDirectoryContent targetData, params FileManagerDirectoryContent[] data)
        {
            return Task.Run(() => MoveToAsync(path, targetPath, names, renameFiles, data)).GetAwaiter().GetResult();
        }

        private async Task<FileManagerResponse> MoveToAsync(string path, string targetPath, string[] names, string[] renamedFiles = null, params FileManagerDirectoryContent[] data)
        {
            FileManagerResponse moveResponse = new FileManagerResponse();
            try
            {
                renamedFiles = renamedFiles ?? new string[0];
                foreach (FileManagerDirectoryContent item in data)
                {
                    if (item.IsFile)
                    {
                        if (await IsFileExists(targetPath + item.Name).ConfigureAwait(false))
                        {
                            int index = -1;
                            if (renamedFiles.Length > 0)
                                index = Array.FindIndex(renamedFiles, Items => Items.Contains(item.Name));
                            if ((path == targetPath) || (index != -1))
                            {
                                string newName = await FileRename(targetPath, item.Name);
                                await MoveItems(rootPath + item.FilterPath, targetPath, item.Name, newName).ConfigureAwait(false);
                                copiedFiles.Add(GetFileDetails(targetPath, item, newName));
                            }
                            else existFiles.Add(item.Name);
                        }
                        else
                        {
                            await MoveItems(rootPath + item.FilterPath, targetPath, item.Name, null).ConfigureAwait(false);
                            copiedFiles.Add(GetFileDetails(targetPath, item, null));
                        }
                    }
                    else
                    {
                        if (!await IsFolderExists(rootPath + item.FilterPath + item.Name))
                            missingFiles.Add(item.Name);
                        else if (await IsFolderExists(targetPath + item.Name))
                        {
                            int index = -1;
                            if (renamedFiles.Length > 0)
                                index = Array.FindIndex(renamedFiles, Items => Items.Contains(item.Name));
                            if ((path == targetPath) || (index != -1))
                            {
                                item.Path = rootPath + item.FilterPath + item.Name;
                                item.Name = await FileRename(targetPath, item.Name);
                                MoveSubFolder(item, targetPath);
                                copiedFiles.Add(GetFileDetails(targetPath, item, null));
                            }
                            else existFiles.Add(item.Name);
                        }
                        else
                        {
                            item.Path = rootPath + item.FilterPath + item.Name;
                            MoveSubFolder(item, targetPath);
                            copiedFiles.Add(GetFileDetails(targetPath, item, null));
                        }
                    }

                }
                moveResponse.Files = copiedFiles;
                if (existFiles.Count > 0)
                {
                    ErrorDetails er = new ErrorDetails();
                    er.FileExists = existFiles;
                    er.Code = "400";
                    er.Message = "File Already Exists";
                    moveResponse.Error = er;
                }
                if (missingFiles.Count > 0)
                {
                    string nameList = missingFiles[0];
                    for (int k = 1; k < missingFiles.Count; k++)
                    {
                        nameList = nameList + ", " + missingFiles[k];
                    }
                    throw new FileNotFoundException(nameList + " not found in given location.");
                }
                await Task.Delay(3800);
                return moveResponse;
            }
            catch (Exception e)
            {
                ErrorDetails er = new ErrorDetails();
                er.Code = "404";
                er.Message = e.Message.ToString();
                er.FileExists = moveResponse.Error?.FileExists;
                moveResponse.Error = er;
                return moveResponse;
            }
        }

        // Search for file(s) or folders
        public FileManagerResponse Search(string path, string searchString, bool showHiddenItems, bool caseSensitive, params FileManagerDirectoryContent[] data)
        {
            Items.Clear();
            FileManagerResponse response = GetFiles(path, data);
            Items.AddRange(response.Files);
            getAllFiles(path, response);
            response.Files = Items.Where(item => new Regex(WildcardToRegex(searchString), (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)).IsMatch(item.Name));
            return response;
        }
        // Gets all files
        protected virtual void getAllFiles(string path, FileManagerResponse data)
        {
            FileManagerResponse directoryList = new FileManagerResponse();
            directoryList.Files = data.Files.Where(item => item.IsFile == false);
            for (int i = 0; i < directoryList.Files.Count(); i++)
            {
                FileManagerResponse innerData = GetFiles(path + directoryList.Files.ElementAt(i).Name + "/", (new[] { directoryList.Files.ElementAt(i) }));
                innerData.Files = innerData.Files.Select(file => new FileManagerDirectoryContent
                {
                    Name = file.Name,
                    Type = file.Type,
                    IsFile = file.IsFile,
                    Size = file.Size,
                    HasChild = file.HasChild,
                    FilterPath = (file.FilterPath)
                });
                Items.AddRange(innerData.Files);
                getAllFiles(path + directoryList.Files.ElementAt(i).Name + "/", innerData);
            }
        }

        protected virtual string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }
        public string ToCamelCase(object userData)
        {
            return JsonConvert.SerializeObject(userData, new JsonSerializerSettings { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } });
        }

        public FileManagerResponse GetFiles(string path, bool showHiddenItems, params FileManagerDirectoryContent[] data)
        {
            throw new NotImplementedException();
        }

        public void setDownloadPath(string downloadLocation)
        {
            throw new NotImplementedException();
        }
    }
}
