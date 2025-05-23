#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Graphics;
using UnityEngine.TestTools.Graphics.Platforms;

public class AdditionalMenuItems : MonoBehaviour
{
    static void CopyFilesToLeafDirectories(string destPath, string srcPath, string[] imageFiles)
    {
        var subDirectories = Directory.GetDirectories(destPath);

        // If this is not a leaf directory then recurse into the subdirectories
        if (subDirectories.Length > 0)
        {
            for (int i = 0; i < subDirectories.Length; i++)
            {
                string subDirectory = subDirectories[i];
                CopyFilesToLeafDirectories(subDirectory + "/", srcPath, imageFiles);
            }
        }
        // We are in a leaf directory so copy all files that do not exist here
        else
        {
            for (int i = 0; i < imageFiles.Length; i++)
            {
                var fullSrcPath = srcPath + imageFiles[i];
                var fullDestPath = destPath + imageFiles[i];

                if (!File.Exists(fullDestPath))
                    File.Copy(fullSrcPath, fullDestPath);
                else
                {
                    System.DateTime srcTime = File.GetLastWriteTimeUtc(fullSrcPath);
                    System.DateTime destTime = File.GetLastWriteTimeUtc(fullDestPath);

                    if(destTime < srcTime)
                        File.Copy(fullSrcPath, fullDestPath, true);
                }
            }
        }
    }

    static string GetSrcReferencePath(bool isRelative)
    {
        var colorSpace = GraphicsTestPlatform.Current.ColorSpace;
        var platform = GraphicsTestPlatform.Current.Platform;
        var graphicsDevice = GraphicsTestPlatform.Current.GraphicsDevice;
        var xrsdk = GraphicsTestPlatform.Current.XrDevice;

        var combinedPath = Path.Combine("ReferenceImages/", string.Format("{0}/{1}/{2}/{3}", colorSpace, platform, graphicsDevice, xrsdk));

        if (!isRelative)
            return Application.dataPath + "/" + combinedPath;
        else
            return "Assets/" + combinedPath;
    }

    [MenuItem("Tests/Duplicate New Reference Images")]
    static void DuplicateNewReferenceItems()
    {
        var srcReferencePath = GetSrcReferencePath(false) + "/";

        var imageFiles = Directory.GetFiles(srcReferencePath, "*.png");

        //Strip the path from the filenames
        for (int i = 0; i < imageFiles.Length; i++)
            imageFiles[i] = Path.GetFileName(imageFiles[i]);

        CopyFilesToLeafDirectories(Application.dataPath + "/ReferenceImages/", srcReferencePath, imageFiles);
    }

    [MenuItem("Assets/Goto Reference Directory", priority = 0)]
    static void SelectImageDirectory()
    {
        var imageFiles = Directory.GetFiles(GetSrcReferencePath(false), "*.png");

        if (imageFiles.Length > 0)
        {
            var filePath = GetSrcReferencePath(true) + "/" + Path.GetFileName(imageFiles[0]);
            var dirObj = AssetDatabase.LoadAssetAtPath(filePath, typeof(UnityEngine.Object));
            Selection.activeObject = dirObj;
        }
    }
}
#endif
