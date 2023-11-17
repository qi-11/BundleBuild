using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

public class BundleBuilder
{
    [MenuItem("Tools/打包工具/清空所有包名",isValidateFunction:false,priority =20)]
    private static void CleanResourcesAssetBundleName()
    {
        string appPath = Application.dataPath + "/";
        string projPath = appPath.Substring(0, appPath.Length - 7);
        string fullPath=projPath+ "/Assets/Resources";


        DirectoryInfo dir = new DirectoryInfo(fullPath);
        var files=dir.GetFiles("*",SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            var filesInfo = files[i];
            string path = filesInfo.FullName.Replace('\\', '/').Substring(projPath.Length) ;
            EditorUtility.DisplayProgressBar("清理打包资源名称","正在处理"+filesInfo.Name,1f*i/files.Length);

            var improter=AssetImporter.GetAtPath(path) ;
            if (improter)
            {
                improter.assetBundleName = null;
            }
        }

        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();

        Debug.Log("=========Clean Lua Bundle Name finished.." + files.Length + "processed");

    }

    [MenuItem("Tools/打包工具/设置资源包名", isValidateFunction: false, priority = 20)]
    private static void SetResourcesAssetBundleName()
    {
        string appPath=Application.dataPath + "/";
        string projPath = appPath.Substring(0,appPath.Length-7);

        string[] searchExtensions = new[] {".prefab",".mat",".txt",".png",".jpg",".shader",".fbx",".controller",".ani","tga" };
        Regex[] excluseRules = new Regex[] { };

        string fullPath = projPath + "/Assets/Resources";

        SetDirAssetBundleName(fullPath,searchExtensions,excluseRules);

        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();

        Debug.Log("=========Set resource bundle name finished....");

    }

    [MenuItem("Tools/打包工具/生成打包文件Android", isValidateFunction: false, priority = 20)]
    private static void BuildAllAssetBundlesAndroid()
    {
        UnityEngine.Debug.Log("=========Build AssetBundles Android start..");
        //用LZ4压缩
        BuildAssetBundleOptions build_options = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.IgnoreTypeTreeChanges | BuildAssetBundleOptions.DeterministicAssetBundle;
        string assetBundleOutputDir=Application.dataPath+"/../AssetBundles/Android/";
        if (!Directory.Exists(assetBundleOutputDir))
        {
            Directory.CreateDirectory(assetBundleOutputDir);
        }

        string projPath = Application.dataPath.Substring(0,Application.dataPath.Length-6);
        BuildPipeline.BuildAssetBundles(assetBundleOutputDir.Substring(projPath.Length),build_options,BuildTarget.Android);

        Debug.Log("=========Build AssetBundles Android finished..");

        GenerateIndexFile(assetBundleOutputDir);
    }

    [MenuItem(itemName: "Tools/打包工具/生成打包文件Windows64", isValidateFunction: false, priority: 21)]
    private static void BuildAllAssetBundlesWindows()
    {
        UnityEngine.Debug.Log("=========Build AssetBundles Window 64 start..");

        //用lz4格式压缩
        BuildAssetBundleOptions build_option = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.IgnoreTypeTreeChanges | BuildAssetBundleOptions.DeterministicAssetBundle;
        string assetBundleOutputDir = Application.dataPath + "/../AssetBundles/Windows/";

        if (!Directory.Exists(assetBundleOutputDir))
        {
            Directory.CreateDirectory(assetBundleOutputDir);
        }

        string projPath = Application.dataPath.Substring(0, Application.dataPath.Length - 6);
        BuildPipeline.BuildAssetBundles(assetBundleOutputDir.Substring(projPath.Length), build_option, BuildTarget.StandaloneWindows64);

        Debug.Log("=========Build AssetBundles Windows 64 finished..");

        GenerateIndexFile(assetBundleOutputDir);
    }

    [MenuItem(itemName: "Tools/打包工具/生成Windows64 Player", isValidateFunction: false, priority: 25)]
    private static void BuildWindowsPlayer()
    {
        BuildPlayerOptions buildPlayerOptions=new BuildPlayerOptions();

        //根据情况修改场景路径名
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/main.unity" };
        buildPlayerOptions.locationPathName = "Win64Player";
        buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
        buildPlayerOptions.options=BuildOptions.None;

        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    [MenuItem(itemName: "Tools/打包工具/生成Android Player", isValidateFunction: false, priority: 25)]
    private static void BuildAndroidPlayer()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();

        //根据情况修改场景路径名
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/main.unity" };
        buildPlayerOptions.locationPathName = "Win64Player";
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = BuildOptions.None;

        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }


    /// <summary>
    /// 遍历目录下的资源文件，生成索引文件
    /// </summary>
    /// <param name="resDir">要遍历的目录</param>
    private static void GenerateIndexFile(string resDir)
    {
        string platName = resDir;
        string projPath = Application.dataPath.Substring(0, Application.dataPath.Length - 6);
        if (platName[platName.Length-1]=='/')
        {
            platName=platName.Substring(0,platName.Length-1);
        }

        platName=platName.Substring(platName.LastIndexOf("/")+1);
        DirectoryInfo dirInfo= new DirectoryInfo(resDir);
        var files=dirInfo.GetFiles("*",SearchOption.AllDirectories);
        List<BundleItem> items=new List<BundleItem>();
        foreach (var file in files)
        {
            if (file.Extension!= ".unity3d" && file.Name!=platName)
            {
                continue;
            }

            BundleItem item = new BundleItem();
            item.m_HashCode = GetFileHash(file.FullName);
            item.m_FileSize = GetFileSize(file.FullName);
            
            item.m_Name = file.FullName.Substring(projPath.Length);
            items.Add(item);
        }

        string idxContent = SaveString(items,resDir);
        string filePath = resDir + "list.txt";
        File.WriteAllText(filePath, idxContent);


        Debug.Log("=========Generated index file to .." + filePath);

    }



    /// <summary>
    /// 设置某个目录及子目录下资源打包名称
    /// </summary>
    /// <param name="fullPath">搜索资源的目录路径</param>
    /// <param name="searchExtensions">要打包的资源扩展名</param>
    /// <param name="excluseRules">要排除掉的资源，用正则表达式</param>
    private static void SetDirAssetBundleName(string fullPath, string[] searchExtensions, Regex[] excluseRules)
    {
        
        if (!Directory.Exists(fullPath))
        {
            return;
        }


        string appPath = Application.dataPath + "/";
        string projPath=appPath.Substring(0,appPath.Length-7);

        DirectoryInfo dir=new DirectoryInfo(fullPath);
        var files = dir.GetFiles("*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            var fileInfo = files[i];

            string ext = fileInfo.Extension.ToLower();
            bool isFound=false;
            foreach (var v in searchExtensions)
            {
                if (ext==v)
                {
                    isFound = true;
                    break;
                }
            }
            if (!isFound)
            {
                continue;
            }

            EditorUtility.DisplayProgressBar("设置打包资源名称", "正在处理" + fileInfo.Name, 1f * i / files.Length);

            string fullName = fileInfo.FullName.Replace('\\','/');
            bool isExcluse = false;
            foreach (Regex excluseRule in excluseRules)
            {
                if (excluseRule.Match(fullName).Success)
                {
                    isExcluse= true;
                    break;
                }
            }

            if (isExcluse)
            {
                continue;
            }

            string path = fileInfo.FullName.Replace('\\', '/').Substring(projPath.Length);
            var importer = AssetImporter.GetAtPath(path);
            if (importer)
            {
                string name=path.Substring(fullPath.Substring(projPath.Length).Length);
                string targetName = "";
                targetName = name.ToLower() + ".unity3d";
                if (importer.assetBundleName!=targetName)
                {
                    importer.assetBundleName = targetName;
                }
            }
        }
    }



    /// <summary>
    /// 获得文件的md5 hash值
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns></returns>
    public static string GetFileHash(string filePath)
    {
        try
        {
            FileStream fs = new FileStream(filePath, FileMode.Open);
            int len = (int)fs.Length;
            byte[] data = new byte[len];
            fs.Read(data, 0, len);
            fs.Close();
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] result = md5.ComputeHash(data);
            string fileMD5 = "";
            foreach (byte b in result)
            {
                fileMD5 += Convert.ToString(b, 16);
            }
            return fileMD5;
        }
        catch (FileNotFoundException e)
        {
            Debug.LogError("can not open file for md5 hash " + e.FileName);
            return "";
        }
    }



    /// <summary>
    /// 获取文件的大小
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public static int GetFileSize(string filePath)
    {
        FileInfo file = new FileInfo(filePath);
        if (file == null)
            return 0;
        return (int)file.Length;
    }



    static public string SaveString(List<BundleItem> list, string path)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var v in list)
        {
            sb.Append(v.m_Name);
            sb.Append('\t');
            sb.Append(v.m_Version);
            sb.Append('\t');
            sb.Append(v.m_HashCode);
            sb.Append('\t');
            sb.Append(v.m_FileSize);
            sb.Append("\r\n");
        }
        return sb.ToString();
    }
}


