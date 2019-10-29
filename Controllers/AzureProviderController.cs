using System.Collections.Generic;
using System.Web.Mvc;
using Newtonsoft.Json;
using Syncfusion.EJ2.FileManager.AzureFileProvider;
using Syncfusion.EJ2.FileManager.Base;
using System.Web;
using System;

namespace EJ2AzureASPMVCFileProvider.Controllers
{
    public class AzureProviderController : Controller
    {
        public ActionResult FileManager()
        {
            return View();
        }

        private AzureFileProvider operation;
        public AzureProviderController()
        {
            operation = new AzureFileProvider();
            operation.RegisterAzure("<--accountName-->", "<--accountKey-->", "<--blobName-->");            
            operation.setBlobContainer("<--blobPath-->", "<--filePath-->");
        }

        public ActionResult AzureFileOperations(FileManagerDirectoryContent args)
        {
            if (args.Path != "")
            {
                string startPath = "<--blobPath-->";
                string originalPath = ("<--filePath-->").Replace(startPath, "");
                args.Path = (originalPath + args.Path).Replace("//", "/");
                args.TargetPath = (originalPath + args.TargetPath).Replace("//", "/");
            }
            switch (args.Action)
            {
                case "read":
                    if (args.Data is null) args.Data = new FileManagerDirectoryContent[0];
                    return Json(operation.ToCamelCase(operation.GetFiles(args.Path, args.Data)));
                case "delete":
                    return Json(operation.ToCamelCase(operation.Delete(args.Path, args.Names, args.Data)));
                case "details":
                    if (args.Names == null)
                    {
                        args.Names = new string[] { };
                    }
                    return Json(operation.ToCamelCase(operation.Details(args.Path, args.Names, args.Data)));
                case "create":
                    return Json(operation.ToCamelCase(operation.Create(args.Path, args.Name)));
                case "search":
                    return Json(operation.ToCamelCase(operation.Search(args.Path, args.SearchString, args.ShowHiddenItems, args.CaseSensitive, args.Data)));
                case "copy":
                    return Json(operation.ToCamelCase(operation.Copy(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data)));
                case "move":
                    return Json(operation.ToCamelCase(operation.Move(args.Path, args.TargetPath, args.Names, args.RenameFiles, args.TargetData, args.Data)));
                case "rename":
                    return Json(operation.ToCamelCase(operation.Rename(args.Path, args.Name, args.NewName, false, args.Data)));
            }
            return null;

        }
        public ActionResult AzureUpload(string path, IList<HttpPostedFileBase> uploadFiles, string action, FileManagerDirectoryContent args)
        {
            if (path == null)
            {
                return Content("");
            }
            if (args.Path != "")
            {
                string startPath = "<--blobPath-->";
                string originalPath = ("<--filePath-->").Replace(startPath, "");
                args.Path = (originalPath + args.Path).Replace("//", "/");
            }
            FileManagerResponse uploadResponse;
            uploadResponse = operation.Upload(args.Path, args.UploadFiles, args.Action, args.Data);
            if (uploadResponse.Error != null)
            {
                HttpResponse Response = System.Web.HttpContext.Current.Response;
                Response.Clear();
                Response.Status = uploadResponse.Error.Code + " " + uploadResponse.Error.Message;
                Response.StatusCode = Int32.Parse(uploadResponse.Error.Code);
                Response.StatusDescription = uploadResponse.Error.Message;
                Response.End();
            }
            return Json("");
        }

        // downloads the selected file(s) and folder(s)
        public object AzureDownload(string downloadInput)
        {
            FileManagerDirectoryContent args = JsonConvert.DeserializeObject<FileManagerDirectoryContent>(downloadInput);
            return operation.Download(args.Path, args.Names, args.Data);
        }

        // gets the image(s) from the given path
        public ActionResult AzureGetImage(FileManagerDirectoryContent args)
        {
            return this.operation.GetImage(args.Path, args.Id, true, null, args.Data);
        }

    }
}
