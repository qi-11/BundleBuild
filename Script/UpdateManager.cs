using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using System;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

public class BundleItem
{
    public int m_Version;
    public int m_FileSize;
    public string m_Name;
    public string m_HashCode;
}


public class UpdateManager : MonoBehaviour
{
    /// <summary>
    /// 热更新阶段
    /// </summary>
    enum UpdateStage
    {
        CheckDownloadIndex,//下载并检查索引文件,生成下载列表
        Downloading,   // 下载需要更新的资源包
        LoadLuaScript,  //加载Lua资源包
    }


    public delegate void ProcessCompleteEvent();

    private UpdateStage m_stage = UpdateStage.CheckDownloadIndex;
    private string m_HttpAddress;
    private static UpdateManager m_Singleton;
    private List<BundleItem> m_DownLoadingList = new List<BundleItem>();
    private int m_CurrenDownloadIndex = 0;//当前下载的文件下标
    private int m_TotalDownloadBytes = 0; //需要下载的所有文件大小
    private int m_AlreadyDownloadBytes = 0; // 已经下载的文件大小
    private WWW mWWW = null;
    private string m_NewIndexContent;//新的索引文件
    private Dictionary<string, byte[]> m_LuaTables = new Dictionary<string, byte[]>();
    private ProcessCompleteEvent m_AllDoneEvent;//文件下载完成之后的回调


    private string BundleIndexFileName = "list.txt"; //打包资源的索引文件

    private string BundleRootDirName = "AssetBundles"; //打包文件所在的根目录名

    private string BundleRootPath = "";
    public static string BundleExtension = "unity3d"; //打包资源扩展名


    public static UpdateManager singleton
    {
        get
        {
            if (m_Singleton == null)
            {
                GameObject go = new GameObject("Update Manager");
                m_Singleton = go.AddComponent<UpdateManager>();
            }
            return m_Singleton;
        }
    }


    /// <summary>
    /// 获取下载进度
    /// </summary>
    public float downLoadingProgress
    {
        get
        {
            int currentBytes = 0;
            if (mWWW != null && m_CurrenDownloadIndex < m_DownLoadingList.Count)
            {
                currentBytes = (int)(m_DownLoadingList[m_CurrenDownloadIndex].m_FileSize * mWWW.progress);
            }

            if (m_TotalDownloadBytes > 0)
            {
                return (float)(m_AlreadyDownloadBytes + currentBytes / (float)m_TotalDownloadBytes);
            }

            return 0;
        }
    }


    /// <summary>
    /// 获取热更新进度
    /// </summary>
    public float totalProgress
    {
        get
        {
            if (m_stage == UpdateStage.CheckDownloadIndex)
            {
                return 0;
            }
            else if (m_stage == UpdateStage.Downloading)
            {
                return 0.1f + downLoadingProgress * 0.8f;
            }
            else if (m_stage == UpdateStage.LoadLuaScript)
            {
                return 0.9f;
            }
            else
            {
                return 1;
            }
        }
    }

    /// <summary>
    /// 开始热更新
    /// </summary>
    public void StartUpdate(string httpServerIP,ProcessCompleteEvent allDoneEv)
    {
        BundleRootPath = Application.persistentDataPath + "/" + BundleRootDirName + "/";
        Debug.Log("start update resources from"+ httpServerIP);

        m_HttpAddress = "http://" + httpServerIP + "/" + BundleRootDirName + "/";
        m_AllDoneEvent = allDoneEv;
        m_stage = UpdateStage.CheckDownloadIndex;

        StartCoroutine(AsyncCheckDownloadingList(OnCompleteCheckDownloadList));
    }

    /// <summary>
    /// 检测完成后的回调
    /// </summary>
    void OnCompleteCheckDownloadList()
    {
        m_stage = UpdateStage.Downloading;

        StartCoroutine(AsyncDownloading(OnCompleteDownloading));
    }


    void OnCompleteDownloading()
    {
        m_stage = UpdateStage.LoadLuaScript;

        StartCoroutine(AsyncLoadLua(OnCompleteLoadLua));
    }

    void OnCompleteLoadLua()
    {
        Debug.Log("update resource complete...");

        m_AllDoneEvent?.Invoke();
    }



    /// <summary>
    /// 从服务器得到资源列表并对比出需要更新的包列表
    /// </summary>
    /// <param name="onCompleteCheckDownloadList">检查完成后的回调</param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    IEnumerator AsyncCheckDownloadingList(ProcessCompleteEvent onCompleteCheckDownloadList)
    {
        //读取本地的索引文件和apk里的索引文件
        Dictionary<string, BundleItem> localbundlesDict = new Dictionary<string, BundleItem>();
        string localIndexPath = BundleRootPath + BundleIndexFileName;

        //如果P目录里没有索引文件，去Resources里拷贝一份到P目录
        if (!File.Exists(localIndexPath))
        {
            UnityEngine.Debug.Log("local idx not found, try copy from default");
            Directory.CreateDirectory(BundleRootPath);
            var txt = Resources.Load(BundleIndexFileName.Substring(BundleIndexFileName.IndexOf('.'))) as TextAsset;
            if (txt != null)
            {
                File.WriteAllText(BundleRootPath + BundleIndexFileName, txt.text);
            }
        }

        if (File.Exists(localIndexPath))
        {
            string indexContent = File.ReadAllText(localIndexPath);
            if (indexContent != null)
            {
                IndexFile file = new IndexFile();
                List<BundleItem> list = file.Load(indexContent);
                foreach (var v in list)
                {
                    localbundlesDict[v.m_Name] = v;
                }
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("local idx not found");
        }

        //下载服务器的索引文件
        WWW www = new WWW(m_HttpAddress + GetBundleManifestName(Application.platform) + "/" + BundleIndexFileName);
        yield return www;


        if (www.error!=null)
        {
            UnityEngine.Debug.Log("remote idx read error " + www.error);
        }

        m_DownLoadingList.Clear();

        if (www.error==null)
        {
            m_NewIndexContent = www.text;
            IndexFile file=new IndexFile();
            List<BundleItem> listServer=file.Load(m_NewIndexContent);
            foreach (var v in listServer)
            {
                string localHash = null;
                string netHash = v.m_HashCode;
                BundleItem localItem = null;
                if (localbundlesDict.TryGetValue(v.m_Name, out localItem))
                {
                    localHash = localItem.m_HashCode;
                }

                if (localHash != netHash)
                {
                    m_DownLoadingList.Add(v); //网上的资源较新则需要重新下载到本地
                }
            }

            UnityEngine.Debug.LogFormat("download idx file success! new bundles count {0}, downloading {1}", listServer.Count, m_DownLoadingList.Count);
        }
        else
        {
            UnityEngine.Debug.LogFormat("download idx file error! {0}", www.error);
        }

        onCompleteCheckDownloadList?.Invoke();

        yield return null;

    }


    /// <summary>
    /// 异步下载需要更新的资源
    /// </summary>
    /// <returns></returns>
    IEnumerator AsyncDownloading(ProcessCompleteEvent ev)
    {
        m_TotalDownloadBytes = 0;
        m_CurrenDownloadIndex = 0;
        m_AlreadyDownloadBytes = 0;

        foreach (var v in m_DownLoadingList)
        {
            m_TotalDownloadBytes += v.m_FileSize;
        }

        foreach (var v in m_DownLoadingList)
        {
            string url = m_HttpAddress + GetBundleManifestName(Application.platform) + "/" + v.m_Name;
            UnityEngine.Debug.LogFormat("downloading {0} size {1}", v.m_Name, v.m_FileSize);
            WWW www = new WWW(url);
            mWWW = www;
            yield return www;
            if (www.error==null)
            {
                string fileName = BundleRootPath + v.m_Name;
                string dir = fileName.Substring(0, fileName.LastIndexOf('/'));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(fileName, www.bytes);
            }
            else
            {
                UnityEngine.Debug.LogErrorFormat("downloading {0} error {1}", v.m_Name, www.error);
            }
            m_AlreadyDownloadBytes += v.m_FileSize;
            m_CurrenDownloadIndex++;
        }

        //全部下载成功后，再覆盖写入索引文件
        Directory.CreateDirectory(BundleRootPath);
        if (m_NewIndexContent != null)
        {
            File.WriteAllText(BundleRootPath + BundleIndexFileName, m_NewIndexContent);
            m_NewIndexContent = null;
        }

        ev?.Invoke();

        yield return null;
    }


    /// <summary>
    /// 从bundle中异步加载lua文件
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    IEnumerator AsyncLoadLua(ProcessCompleteEvent ev)
    {
        string luaDir = BundleRootPath + "lua/";
        DirectoryInfo dir= new DirectoryInfo(luaDir);
        var files=dir.GetFiles("*",SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            var fileInfo = files[i];
            string ext = fileInfo.Extension.ToLower();

            if (ext != BundleExtension)
            {
                continue;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(fileInfo.FullName);
            if (bundle == null)
            {
                continue;
            }

            AssetBundleRequest request = bundle.LoadAllAssetsAsync();
            yield return request;

            if (request.allAssets.Length == 0)
            {
                continue;
            }

            var text = request.allAssets[0] as TextAsset;
            if (text == null)
            {
                continue;
            }

            string name = fileInfo.FullName.Substring(luaDir.Length);
            name = name.Remove(name.LastIndexOf('.')); //去掉.unity3d
            name = name.Remove(name.LastIndexOf('.')); //去掉.lua
            m_LuaTables[name] = text.bytes;

        }

        ev?.Invoke();

        yield return null;

    }



    internal byte[] GetLuaBytes(string name)
    {
        var subName = name.Substring(name.LastIndexOf('/') + 1);
        if (m_LuaTables.ContainsKey(subName))
        {
            return m_LuaTables[subName];
        }

        if (m_LuaTables.ContainsKey(name))
        {
            return m_LuaTables[name];
        }

        TextAsset txtAsset = Resources.Load<TextAsset>("Lua/" + name + ".lua");
        if (txtAsset != null)
        {
            return txtAsset.bytes;
        }

        return null;
    }









    /// <summary>
    /// 根据平台不同获取打包依赖关系文件名
    /// </summary>
    /// <param name="plat"></param>
    /// <returns></returns>
    public static string GetBundleManifestName(RuntimePlatform plat)
    {
        if (plat == RuntimePlatform.WindowsEditor || plat == RuntimePlatform.WindowsPlayer)
        {
            return "Windows";
        }
        else if (plat == RuntimePlatform.Android)
        {
            return "Android";
        }
        else if (plat == RuntimePlatform.IPhonePlayer)
        {
            return "IOS";
        }
        else
        {
            return "Windows";
        }
    }
}

