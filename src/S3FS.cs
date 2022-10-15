using System;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Security.Principal;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;
using System.Xml;
using System.Text;

using NeoGeo.Library.SMB.Provider;
using Affirma.ThreeSharp.Wrapper;
using Affirma.ThreeSharp.Model;
using Affirma.ThreeSharp.Query;
using Affirma.ThreeSharp;

namespace Suchwerk.Filesystem
{

	/// <summary>
	/// Erweiterung der Basisklasse FileContext
	/// </summary>
	public class S3FileContext : FileContext
	{
		internal FileStream FileStream = null;

		internal S3FileContext(string name, bool isDirectory, FileStream fileStream) : base(name, isDirectory)
		{
			FileStream = fileStream;
		}

		~S3FileContext()
		{
			if( FileStream != null )
			{
				try
				{
					FileStream.Flush();
					FileStream.Close();
				}
				catch
				{
				}

				FileStream = null;
			}
		}
	}

	public class S3FS : FileSystemProvider, IDisposable
	{
		private string _FileSystemProviderType;
		
		/// <summary>
		/// Base directory of the share
		/// </summary>
		private string _mappedPath;
		//private string _realPath;
		private long _totalbucketsize=0;
		//private static int instance=0;
		
		private static string _awsAccessKeyId;
		private static string _awsSecretAccessKey;

		private ThreeSharpConfig ThreeConfig=new ThreeSharpConfig();
		private IThreeSharp ThreeService;

		private XmlDocument objDocument = new XmlDocument();
		private XmlDocument contentDocument = new XmlDocument();


		public override string FileSystemProviderType
		{
			get { return _FileSystemProviderType; }
			set { _FileSystemProviderType = value; }
		}

		public override void Initialize( string name, NameValueCollection config )
		{
			// Verify that config isn't null
			if (config == null)
			throw new ArgumentNullException("config");
			
			// Assign the  provider a default name if it doesn't have one
			if (String.IsNullOrEmpty(name))
			name = "S3FSProvider";
			//_realPath=name;
			
			// Add a default "description" attribute to config if the
			// attribute doesn't exist or is empty
			if (string.IsNullOrEmpty(config["description"]))
			{
				config.Remove("description");
				config.Add("description","S3FS provider");
			}
			
			// Call the  base class's Initialize method
			base.Initialize(name, config);

			// Initialize _FileSystemProviderType
			_FileSystemProviderType = config["filesystemprovidertype"];

			if (string.IsNullOrEmpty(_FileSystemProviderType))
			_FileSystemProviderType = this.GetType().ToString();

			config.Remove("filesystemprovidertype");

			//System.Text.StringBuilder msgboxdata = new System.Text.StringBuilder(260);
			//msgboxdata.Append(name);
			//msgboxdata.Append("\n");			
			//for (int looper=0;looper<config.Count;++looper) {
			//	msgboxdata.Append(config.GetKey(looper));
			//	msgboxdata.Append("\n");
			//Debug.WriteLine("\n");
			//}

			//instance++;
			//msgboxdata.Remove(0,msgboxdata.Length);
			
			// Initialize awsKeys
			_awsAccessKeyId=config["awsAccessKeyId"];
			string description; //should be "AWSKEY" after decrypt
			_awsSecretAccessKey=DPAPI.Decrypt(config["awsSecretAccessKey"], WindowsIdentity.GetCurrent().User.ToString(), out description);
			//_awsSecretAccessKey=config["awsSecretAccessKey"];
			if (String.IsNullOrEmpty(_awsAccessKeyId) || String.IsNullOrEmpty(_awsSecretAccessKey))
			throw new ProviderException("Empty or missing awsKeys");
			config.Remove("awsAccessKeyId");
			config.Remove("awsSecretAccessKey");
			
			//ThreeConfig = new ThreeSharpConfig();
			ThreeConfig.AwsAccessKeyID = _awsAccessKeyId;
			ThreeConfig.AwsSecretAccessKey = _awsSecretAccessKey;
			ThreeService = new ThreeSharpQuery(ThreeConfig);

			//Get a list of s3 buckets
			
			try {
				using(BucketListRequest request = new BucketListRequest(null))
				using(BucketListResponse response = ThreeService.BucketList(request))
				{
					objDocument.LoadXml(response.StreamResponseToString());
				}
				//objDocument.LoadXml(wrapper.ListBucket(null));
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return; //if we cannot process the credentials for this user then we ignore.
			}
			//MessageBox.Show(wrapper.ListBucket(null), "Buckets", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			/*
			String allBuckets="Config Name:\n"+name;

			allBuckets+="\nUser:\n";
			XmlNodeList elemList = objDocument.GetElementsByTagName("DisplayName");
			for (int i=0; i < elemList.Count; i++)
			{   
				allBuckets+=(elemList[i].InnerXml)+"\n";
			}

			allBuckets+="\nBuckets:\n";
			elemList = objDocument.GetElementsByTagName("Name");
			for (int i=0; i < elemList.Count; i++)
			{   
				allBuckets+=(elemList[i].InnerXml)+"\n";
			}
			MessageBox.Show(allBuckets, "Buckets", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			*/
			//
			//todo:  we are passed from the .config file, description="S3 sharing" mappedpath="temp" and our keys although the entire line looks like:
			//<add name="joelhewitt" description="S3 sharing" type="Suchwerk.FileSystem.S3FS, S3FS" mappedpath="temp" awsAccessKeyId="" awsSecretAccessKey="" />
			//we can ignore the mapped path, perhaps it remove it later, make _mappedPath and array, fill it with the bucket info.  but we add code further down to look where the user is browsing 
			//and return info from the buckets
			
			// Initialize _mappedPath
			string MappedPath = config["mappedpath"];

			if (String.IsNullOrEmpty(MappedPath))
			throw new ProviderException("Empty or missing mappedpath");

			_mappedPath = MappedPath;
			
			config.Remove("mappedpath");

			// Throw an exception if unrecognized attributes remain
			if (config.Count > 0)
			{
				string attr = config.GetKey(0);
				if (!String.IsNullOrEmpty(attr))
				throw new ProviderException("Unrecognized attribute: " + attr);
			}
		}

		public S3FS()
		{
		}

		public override bool CanSeeShare(string shareName, UserContext userContext)
		{
			//File.AppendAllText("wins3fs-log.txt",_mappedPath+" CanSeeShare"+Environment.NewLine);
			
			return base.CanSeeShare(shareName, userContext);
		}

		public override NT_STATUS ReadDirectory(UserContext UserContext, FileContext fileContext)
		{
			NT_STATUS error = NT_STATUS.OK;
			
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" ReadDirectory"+Environment.NewLine);

			FileContext S3Context = fileContext;
			
			if (!S3Context.IsDirectory)
			{
				Debug.WriteLine("Warnung->Handle verweißt nicht auf ein Verzeichnis");
				File.AppendAllText("wins3fs-log.txt"," !S3Context.IsDirectory"+Environment.NewLine);
				return NT_STATUS.INVALID_HANDLE;
			}

			//if (!Directory.Exists(_mappedPath + S3Context.Name))
			//{
			// Directory not found, sollte nie passieren
			//File.AppendAllText("wins3fs-log.txt"," !Directory.Exists"+Environment.NewLine);
			//return NT_STATUS.OBJECT_PATH_NOT_FOUND;
			//}

			//S3Context.Items.Add(new DirectoryContext(".", FileAttributes.Directory));
			//S3Context.Items.Add(new DirectoryContext("..", FileAttributes.Directory));

			using(BucketListRequest request = new BucketListRequest(_mappedPath))
			using(BucketListResponse response = ThreeService.BucketList(request))
			{
				contentDocument.LoadXml(response.StreamResponseToString());
			}
			//			contentDocument.LoadXml(wrapper.ListBucket(_mappedPath));
			XmlNodeList ContentsKey = contentDocument.GetElementsByTagName("Key");
			XmlNodeList ContentsDate = contentDocument.GetElementsByTagName("LastModified");
			XmlNodeList ContentsSize = contentDocument.GetElementsByTagName("Size");
			DirectoryContext Item = new DirectoryContext();
			_totalbucketsize=0;
			//int bucket_found=0;
			
			for (int i=0; i < ContentsKey.Count; i++)
			{ 
				Item = new DirectoryContext();

				//File.AppendAllText("wins3fs-log.txt",ContentsKey[i].InnerXml+" "+ContentsSize[i].InnerXml+" "+ContentsDate[i].InnerXml+Environment.NewLine);
				Item.Attrib = FileAttributes.Normal; //| DI.Attributes;
				String bDate=ContentsDate[i].InnerXml.Substring(5,2)+"/"+ContentsDate[i].InnerXml.Substring(8,2)+"/"+ContentsDate[i].InnerXml.Substring(0,4);
				String bTime=ContentsDate[i].InnerXml.Substring(11,2)+":"+ContentsDate[i].InnerXml.Substring(14,2)+":"+ContentsDate[i].InnerXml.Substring(17,2);
				Item.CreationTime = Convert.ToDateTime(bDate+" "+bTime); //Convert.ToDateTime(ContentsDate[i].InnerXml);
				Item.LastAccessTime = Item.CreationTime;
				Item.LastWriteTime = Item.CreationTime;
				Item.FileSize = Convert.ToInt32(ContentsSize[i].InnerXml);
				_totalbucketsize += Item.FileSize;
				Item.Name = ContentsKey[i].InnerXml;
				Item.ShortName = GetShortName(ContentsKey[i].InnerXml);
				
				S3Context.Items.Add_MatchAlreadChecked(Item);
			}
			


			//ignore sub directories for now, lets focus on files			
			/*
			foreach (string DirName in Directory.GetDirectories(_mappedPath + S3Context.Name, S3Context.Filter))
			{
				error = GetAttributes(UserContext, DirName.Substring(_mappedPath.Length), out Item); //, SearchFlag.Dir);
				if (error != 0)
				Trace.WriteLine("Warning->Error: " + error + " during listing directories: " + DirName);

				S3Context.Items.Add_MatchAlreadChecked(Item);
			}
*/
			/*
			foreach (string FileName in Directory.GetFiles(_mappedPath + S3Context.Name, S3Context.Filter))
			{
				error = GetAttributes(UserContext, FileName.Substring(_mappedPath.Length), out Item); //, SearchFlag.File);
				if (error != 0)
				Trace.WriteLine("Warning->Error: " + error + " during listing files: " + FileName);

				S3Context.Items.Add_MatchAlreadChecked(Item);
			}
*/			
			return error;
		}

		
		public override NT_STATUS DeleteDirectory(UserContext UserContext, string Path)
		{
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" DeleteDirectory"+Environment.NewLine);

			if (Path == "")
			return NT_STATUS.ACCESS_DENIED;                 // DaAs Root-Verzeichnis darf nicht gelöscht werden

			if (!System.IO.Directory.Exists(_mappedPath + Path))
			return NT_STATUS.OBJECT_PATH_NOT_FOUND;			// Dir not found

			if ((new DirectoryInfo(_mappedPath + Path).Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
			return NT_STATUS.ACCESS_DENIED;
			
			try
			{
				System.IO.Directory.Delete(_mappedPath + Path, false);
			}
			catch (UnauthorizedAccessException ex)
			{
				Trace.WriteLine("Warning->Fehler bei Löschen des Verzeichnisses '" + Path + "', es eine Exeption aufgetreten: " + ex.Message);
				return NT_STATUS.ACCESS_DENIED;     // Nachdem wir oben schon festgestellt haben dass das Verzeichnis da ist,
				// kann es nur noch mal Zugriff liegen
			}
			catch (IOException ex)
			{
				Trace.WriteLine("Warning->Fehler bei Löschen des Verzeichnisses '" + Path + "', es eine Exeption aufgetreten: " + ex.Message);
				return NT_STATUS.DIRECTORY_NOT_EMPTY;   // Nachdem wir oben schon festgestellt haben dass das Verzeichnis da ist,
				// und nicht read-only ist, kann es nur noch nicht leer ist. 
			}
			return NT_STATUS.OK;
		}

		public override NT_STATUS CreateDirectory(UserContext UserContext, string Path, FileAttributes Attributes)
		{
			// Dir not found as no dir is the path
			
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" CreateDirectory"+Environment.NewLine);
			//since the bucket info is loaded at program startup, we might need to re-run the config program, and then restart the daemon.
			//if we are creating a directory in S3, then no restart is needed.

			if (Path.IndexOf("\\") == -1)
			return NT_STATUS.OBJECT_PATH_NOT_FOUND;

			// File/Directory already exists
			if (Directory.Exists(_mappedPath + Path) || FileEx.Exists(_mappedPath + Path))
			return NT_STATUS.OBJECT_NAME_COLLISION;  

			Directory.CreateDirectory(_mappedPath + Path);

			if (Attributes != FileAttributes.Normal)
			{
				DirectoryInfo DI = new DirectoryInfo(_mappedPath + Path);
				DI.Attributes = Attributes;
			}

			return NT_STATUS.OK;
		}

		public override NT_STATUS FSInfo(UserContext UserContext, out FileSystemAttributes data)
		{
			// Should be implemented very fast, as this method is called quite often
			// Try to implement is without any I/O or cache the values. 
			
			base.FSInfo(UserContext, out data);
			data.FSName = "S3FS";
			
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" FSInfo "+data+Environment.NewLine);

			data.SectorUnit = 1;						    // FreeBytes and TotalBytes will me multiplied by this value
			data.Sectors = 1;							    // FreeBytes and TotalBytes will be multiplied by this value
			//DriveInfo DI = new DriveInfo(_mappedPath.Substring(0, 2));
			//data.FreeBytes = DI.TotalFreeSpace*2;
			data.FreeBytes = 281474976710656L; //1125899906842624L; //0x4000000000000; //1<<50
			//data.TotalBytes = DI.TotalSize*2;
			data.TotalBytes = _totalbucketsize; ////0x4000000000000; //1<<50

			//If you activate this, you must define one of the constants in AlternativeStreams.cs
			data.FSAttributes |= FILE_FS_ATTRIBUTE_INFORMATION.FILE_NAMED_STREAMS;

			data.ObjectID = new byte[16];
			for (int i = 0; i < data.FSName.Length && i < 16; i++)
			data.ObjectID[i] = (byte)data.FSName[i];
			
			return NT_STATUS.OK;
		}

		public override NT_STATUS DeviceIO(UserContext UserContext, FileContext fileContext, int Command, bool IsFsctl, ref byte[] Input, ref byte[] Output, ref int ValidOutputLength)
		{
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" IOCTL"+Environment.NewLine);


			/*
			* Calls the local IOCRTL function, see DeviceIOControl: http://msdn2.microsoft.com/en-us/library/aa364230.aspx
			* 
			* Here are three ways to implement it:
			* 1. Do nothing and report an error, nearly all client accept this
			* 2. If you deal with files on a filesystem that supports the IOCRTL function, call it and return the result
			* 3. Create your own IOCRTL function and parameter. Do this if you want to show a client information about a file that is normally not available
			*    Write a program on the client that calls this function, e.g. a right click handler and call the IOCRTOL function
			*    Repsonde with the requested values
			* 
			* Find sample implementation for all three cases
			*/

			// Select the case you want to try
			int Case = 3;

			switch (Case)
			{
			case 1: // We ignore the call
				Trace.WriteLine("Warning->IOCTL is not implemented, the called method was: 0x" + Command.ToString("X8"));
				return NT_STATUS.NOT_IMPLEMENTED;

			case 2: // We forward the call to the underlaying file system, not alwasy possible, e.g. file stored in a SQL server or at Amazon
				//http://wiki.ethereal.com/SMB2/Ioctl/Function/

				// Get the internal FileContext, the FileStream contains the old Win32 file handle
				// In another filesytem implementation it could be necessary to open the file with the CreateFile method, see DeviceIO
				// If you open the file, do not forget to close the file with the CloseHandle method.
				S3FileContext S3Context = (S3FileContext)fileContext;

				if (S3Context.FileStream == null)
				return NT_STATUS.INVALID_DEVICE_REQUEST;  // The file is not open yet, so we fail

				bool ret = DeviceIO_Unmanaged.DeviceIoControl(S3Context.FileStream.SafeFileHandle, (DeviceIO_Unmanaged.EIOControlCode)Command, ref Input, ref Output, ref ValidOutputLength);

				if (!ret)
				{
					return NT_STATUS.INVALID_DEVICE_REQUEST;
					//return System.Runtime.InteropServices.Marshal.GetLastWin32Error();
				}
				return 0;

			case 3: // We implement some of the usaual command on our own
				//http://wiki.ethereal.com/SMB2/Ioctl/Function/
				switch (Command)
				{
				case 0x00090028: // FSCTL_IS_VOLUME_MOUNTED
					ValidOutputLength = 0;
					return 0;           // Return no error as the Filesystem is here

					// see http://wiki.ethereal.com/SMB2/Ioctl/Function/FILE_DECIVE_FILE_SYSTEM/FSCTL_CREATE_OR_GET_OBJECT_ID
				case 0x000900C0: // FSCTL_CREATE_OR_GET_OBJECT_ID = EIOControlCode.FsctlCreateOrGetObjectId

					// We have to return a buffer in the format: http://wiki.ethereal.com/SMB2/Ioctl/FILE_OBJECTID_BUFFER
					//00,16  byte[16]   Object ID: GUID of the object
					//16,16  byte[16]   Birth Volume ID: GUID of the volume
					//32,16  byte[16]   Birth Object ID: GUID of the object when it was originally created
					//48,16  byte[16]   Doamin ID, is always 0
					//64
					if (Output.Length < 64)
					return NT_STATUS.BUFFER_TOO_SMALL;

					// It would be easier if the file is stored on e.g a SQL server, than we could 
					// use the row-id or a key-value for Object ID and the name of the server as volume id, ...

					// Here we fake something very easy, we just use the CreateDate as the Object ID
					// and set a constant birth volume name

					// That is not very clever as many files can have the same creation date, especially if you copy them.
					// To add some information from the name or path is not possible, as the id should not(!) change 
					// when then file rename or moved

					BitConverter.GetBytes(FileEx.GetCreationTimeUtc(fileContext.Name).Ticks).CopyTo(Output, 0);
					// The last 8 bytes of the object ID are always 0, not very clever

					// We just code a name, not very fancy
					string VolumeName = "S3-FS";
					for (int i = 0; i < VolumeName.Length && i < 16; i++)
					Output[16 + i] = (byte)VolumeName[i];

					// We set the birth object id to the same value as the object id
					BitConverter.GetBytes(FileEx.GetCreationTimeUtc(fileContext.Name).Ticks).CopyTo(Output, 32);

					ValidOutputLength = 64;
					return 0;
				default:
					Trace.WriteLine("Warning->IOCTL is implemented, but this method not: 0x" + Command.ToString("X8"));
					//Debugger.Break();
					return NT_STATUS.NOT_IMPLEMENTED;	
				}
			}

			return NT_STATUS.NOT_IMPLEMENTED;	
		}

		public override NT_STATUS Close(FileContext fileContext, DateTime LastWriteTime)
		{
			// If you use a write cache, be sure to call flush or any similar method to write the data through. 
			// The close should be done within 20 secounds and after all data is at the final media stored
			S3FileContext hinfo = (S3FileContext)fileContext;
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Close filecontext"+fileContext.Name+Environment.NewLine);

			if (hinfo.FileStream != null)
			hinfo.FileStream.Close();

			try
			{
				if (hinfo.IsDirectory)
				Directory.SetLastWriteTime(_mappedPath + hinfo.Name, LastWriteTime);
				else
				FileEx.SetLastWriteTime(_mappedPath + hinfo.Name, LastWriteTime);
				File.Delete(System.IO.Path.GetTempPath()+fileContext.Name);
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warning->Beim Setzen der LastWriteTime für '" + hinfo.Name + "' ist in Close eine Exeption aufgetreten: " + ex.Message);
			}

			return NT_STATUS.OK;
		}

		public override NT_STATUS Close(FileContext fileContext)
		{
			S3FileContext hinfo = (S3FileContext)fileContext;
			
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Close"+fileContext.Name+Environment.NewLine);
			if (hinfo.FileStream != null)
			hinfo.FileStream.Close();
			
			try{
				File.Delete(System.IO.Path.GetTempPath()+fileContext.Name);
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warning->Beim Setzen der LastWriteTime für '" + hinfo.Name + "' ist in Close eine Exeption aufgetreten: " + ex.Message);
			}


			return NT_STATUS.OK;
		}

		public override NT_STATUS GetService(out string Service, out string NativeFileSystem, out string Comment)
		{
			base.GetService(out Service, out NativeFileSystem, out Comment);

			Comment = "S3 mapping of " + _mappedPath;
			//File.AppendAllText("wins3fs-log.txt",_mappedPath+" GetService "+ Service+" "+NativeFileSystem+ " "+Comment+Environment.NewLine);

			return NT_STATUS.OK;
		}

		public override NT_STATUS Create(UserContext UserContext, string Name, SearchFlag Flags, FileMode Mode, FileAccess Access, FileShare Share, FileAttributes Attributes, out FileContext fileContext)
		{
			fileContext = null;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Create "+Name+Environment.NewLine);

			NT_STATUS error = NT_STATUS.OK;

			string PathName = _mappedPath + Name;

			try
			{
				switch (Mode)
				{
				case FileMode.Open:			// Both work only if the file exists
				case FileMode.Truncate:
					File.AppendAllText("wins3fs-log.txt","Open | Truncate "+Environment.NewLine);

					switch (Flags)
					{
					case SearchFlag.FileAndDir:
						File.AppendAllText("wins3fs-log.txt","SearchFlag.FileAndDir "+Environment.NewLine);
						//check to see if the file exists.
						if (FileEx.Exists(PathName))
						{
							fileContext = new S3FileContext(Name, false, FileEx.Open(PathName, Mode, Access, Share));
							return NT_STATUS.OK;
						}
						if (Directory.Exists(PathName))
						{
							fileContext = new S3FileContext(Name, true, null);
							return NT_STATUS.OK;
						}
						return NT_STATUS.NO_SUCH_FILE;
					case SearchFlag.File:
						File.AppendAllText("wins3fs-log.txt","SearchFlag.File "+Name+"|"+PathName+" "+Environment.NewLine);
						XmlNodeList ContentsKey = contentDocument.GetElementsByTagName("Key");
						
						for (int i=0; i < ContentsKey.Count; i++)
						{ 
							if (ContentsKey[i].InnerXml==Name.Substring(1,Name.Length-1))
							{
								File.AppendAllText("wins3fs-log.txt","trying to open: "+System.IO.Path.GetTempPath()+ContentsKey[i].InnerXml+Environment.NewLine);
								//fileContext = new S3FileContext(Name, false, File.Open(PathName, Mode, Access, Share));
								//System.IO.Path.GetTempPath()+ContentsKey[i].InnerXml
								//FileStream fs=File.Open(System.IO.Path.GetTempPath()+ContentsKey[i].InnerXml,FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read); //touch the file
								//fs.Close();
								//fileContext = new S3FileContext(Name, false, File.Open(System.IO.Path.GetTempPath()+ContentsKey[i].InnerXml, Mode|FileMode.Open, Access|FileAccess.Read, Share|FileShare.Read)); //TODO maybe put a  memory mapped file for data interchange...
								//fileContext = new S3FileContext(Name, false, File.Open(System.IO.Path.GetTempPath()+ContentsKey[i].InnerXml,FileMode.Open, FileAccess.Read, FileShare.Read)); //TODO maybe put a  memory mapped file for data interchange...
								fileContext = new S3FileContext(Name, false, null); //TODO maybe put a  memory mapped file for data interchange...
								return NT_STATUS.OK;
							}
						}
						return NT_STATUS.NO_SUCH_FILE; ;
					case SearchFlag.Dir:
						File.AppendAllText("wins3fs-log.txt","SearchFlag.Dir "+Environment.NewLine);
						
						XmlNodeList elemList = objDocument.GetElementsByTagName("Name");
						for (int i=0; i < elemList.Count; i++)
						{ 
							if (elemList[i].InnerXml==_mappedPath) {
								//File.AppendAllText("wins3fs-log.txt","FOUND XML "+Environment.NewLine);
								fileContext = new S3FileContext(Name, true, null);
								return NT_STATUS.OK;
							}
						}
						return NT_STATUS.OBJECT_PATH_NOT_FOUND;
					default:
						return NT_STATUS.INVALID_PARAMETER;
					}

				case FileMode.CreateNew:
					File.AppendAllText("wins3fs-log.txt","CreateNew "+Environment.NewLine);

					// Works only if the file does not exists
					if (FileEx.Exists(PathName))
					return NT_STATUS.OBJECT_NAME_COLLISION;	// Access denied as it is already there
					if (Access == FileAccess.Read)              // Office 2003 makes a stupid call of: "CreateNew with Read access", C# refuse to execute it!
					Access = FileAccess.ReadWrite;

					fileContext = new S3FileContext(Name, false, FileEx.Open(PathName, Mode, Access, Share));
					return NT_STATUS.OK;

				case FileMode.Create:
				case FileMode.OpenOrCreate:
					File.AppendAllText("wins3fs-log.txt","Create | OpenOrCreate"+Environment.NewLine);
					
					// Use existing file if possible otherwise create new

					/* For some strange reasons WinWord and other programs like smbtorture send a 
						* "Mode" of Create with "Access" of Read
						* which in my opinion do not make much sense. Also the developer of .NET do not allow that 
						* combination in the File.Open method, so we have to change the Access to Read/Write
						*/
					if (Mode == FileMode.Create && Access == FileAccess.Read)
					{
						Access = FileAccess.ReadWrite;
						Debug.WriteLine("Info->Change the FileAccess from 'Read' to 'RearWrite' as the FileMode was 'Create'");
					}
					//fileContext = new S3FileContext(Name, false, FileEx.Open(System.IO.Path.GetTempPath()+PathName, Mode, Access, Share));
					File.AppendAllText("wins3fs-log.txt",System.IO.Path.GetTempPath()+Name+Environment.NewLine);
					fileContext = new S3FileContext(Name, false, FileEx.Open(System.IO.Path.GetTempPath()+Name, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite));
					return NT_STATUS.OK;
				default:
					return NT_STATUS.INVALID_PARAMETER;
				}
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warning->Exception beim Öffnen des FileStreams: " + ex.Message);

				error = (NT_STATUS)Marshal.GetHRForException(ex);	
				if ((uint)error == 0x80070020)
				error = NT_STATUS.SHARING_VIOLATION;
				else
				Debug.WriteLine("Warning->Reporting S3 error 'No such file'");
				return NT_STATUS.NO_SUCH_FILE;
			}

		}

		public override NT_STATUS Rename(UserContext UserContext, string OldName, string NewName)
		{
			NT_STATUS error = NT_STATUS.OK;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Rename"+Environment.NewLine);


			
			if (Directory.Exists(_mappedPath + OldName))
			{
				// We are in Directory case
				if (Directory.Exists(_mappedPath + NewName))
				return NT_STATUS.OBJECT_NAME_COLLISION;
				if (FileEx.Exists(_mappedPath + NewName))
				return NT_STATUS.OBJECT_NAME_COLLISION;
				try
				{
					Directory.Move(_mappedPath + OldName, _mappedPath + NewName);
				}
				catch (Exception e)
				{
					Trace.WriteLine("Warnung->Exception bei Rename von Verzeichnissen: " + e.Message);
					error = (NT_STATUS)Marshal.GetHRForException(e);
					//error = 3;					// Verzeichnisfehler
				}
			}
			else
			{	// We are in File case
				if (!FileEx.Exists(_mappedPath + OldName))
				return NT_STATUS.OBJECT_NAME_NOT_FOUND;		// Orginalname nicht da
				if (FileEx.Exists(_mappedPath + NewName))
				return NT_STATUS.OBJECT_NAME_COLLISION;
				try
				{
					FileEx.Move(_mappedPath + OldName, _mappedPath + NewName);
				}
				catch (Exception e)
				{
					Trace.WriteLine("Warnung->Exception bei Rename von Datei: " + e.Message);
					error = (NT_STATUS)Marshal.GetHRForException(e);
					
					if ((uint)error == 0x80070020)
					error = NT_STATUS.SHARING_VIOLATION;
				}
			}
			return error;
		}

		public override NT_STATUS Delete(UserContext UserContext, string FileName)
		{
			NT_STATUS error = NT_STATUS.OK;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Delete"+Environment.NewLine);
			
			string OrginalName = _mappedPath + FileName;

			Debug.WriteLine("Info->Datei '" + FileName + "' wird als '" + OrginalName + "' von der Platte gelöscht.");

			try
			{
				if (!FileEx.Exists(OrginalName))
				return NT_STATUS.OBJECT_NAME_NOT_FOUND;
				FileEx.Delete(OrginalName);
			}
			catch (Exception e)
			{
				Trace.WriteLine("Warnung->Exception bei Delete: " + e.Message);
				error = (NT_STATUS)Marshal.GetHRForException(e);					// ERROR_READ_FAULT
				if ((uint)error == 0x80070020)
				error = NT_STATUS.SHARING_VIOLATION;
			}

			return error;
		}

		public override NT_STATUS Flush(UserContext UserContext, FileContext fileContext)
		{
			// Will not be called very often, but make sure that the call returns after the data is writen through the final media

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Flush"+Environment.NewLine);
			
			S3FileContext S3Context = (S3FileContext)fileContext;

			if (S3Context.IsDirectory || S3Context.FileStream == null)
			return NT_STATUS.OK;						// or ERROR_INVALID_HANDLE

			try
			{
				S3Context.FileStream.Flush();
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warnung->Exception bei Flush: " + ex.Message);
				return (NT_STATUS)Marshal.GetHRForException(ex);
				//return 29;					// ERROR_WRITE_FAULT
			}

			return NT_STATUS.OK;
		}

		public override NT_STATUS Write(UserContext UserContext, FileContext fileContext, long Offset, ref int Count, ref byte[] Buffer, int Start)
		{
			// All locking issues are handled in the calling class, except if other application accesses the files from outside 
			// WinFUSE

			// It's possible to write all data to a cache and write it through to the final media after a flush or close. But this 
			// write through should not last longer than 20 secounds
			NT_STATUS error = NT_STATUS.OK;

			//File.AppendAllText("wins3fs-log.txt",_mappedPath+" Write "+Offset.ToString()+" count "+Count.ToString()+" Start "+Start.ToString()+Environment.NewLine);
			
			S3FileContext S3Context = (S3FileContext)fileContext;

			if (S3Context.IsDirectory || S3Context.FileStream == null)
			{
				Debug.WriteLine("Warnung->Aus einem Verzeichnis kann nicht gelesen werden");
				Count = 0;
				return NT_STATUS.INVALID_HANDLE;						// ERROR_INVALID_HANDLE
			}

			if (!S3Context.FileStream.CanWrite && !S3Context.FileStream.CanSeek)
			{
				Debug.WriteLine("Warnung->Das der Datei kann nicht geschrieben werden.");
				Count = 0;
				return NT_STATUS.INVALID_PARAMETER;						// ERROR_INVALID_PARAMETER;
			}

			if (Count > 0x0FFFFFFF)
			{
				Debug.WriteLine("Warnung->Anzahl der zu schreibenen Bytes ist zu groß.");
				Count = 0;
				return NT_STATUS.INVALID_PARAMETER;						//ERROR_INVALID_PARAMETER
			}

			long NewOffset;
			try
			{
				NewOffset = S3Context.FileStream.Seek(Offset, System.IO.SeekOrigin.Begin);
				if (NewOffset != Offset)
				{
					Debug.WriteLine("Warnung->Von der angegebenen Position kann nicht geschrieben werden.");
					Count = 0;
					return NT_STATUS.INVALID_PARAMETER;                 //132 ERROR_SEEK_ON_DEVICE
				}

				BinaryWriter Writer = new BinaryWriter(S3Context.FileStream);

				Writer.Write(Buffer, Start, Count);
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warnung->Exception bei Write: " + ex.Message);
				Count = 0;
				//error = 29;					// ERROR_WRITE_FAULT
				error = (NT_STATUS)Marshal.GetHRForException(ex);
			}
	
			if (S3Context.FileStream.Length==Offset+Count) {
				File.AppendAllText("wins3fs-log.txt","done "+S3Context.FileStream.Length.ToString()+Environment.NewLine);
				String keyname=fileContext.Name.Substring(1,fileContext.Name.Length-1);
				S3Context.FileStream.Close();
				using (ObjectAddRequest request = new ObjectAddRequest(_mappedPath, keyname))
				{
					request.Headers.Add("x-amz-acl", "public-read");
					request.LoadStreamWithFile(System.IO.Path.GetTempPath()+fileContext.Name);
					using (ObjectAddResponse response = ThreeService.ObjectAdd(request))
					{ }
				}
			}
			return error;
		}

		public override NT_STATUS Lock(UserContext UserContext, FileContext fileContext, long Offset, long Count)
		{
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Lock"+Environment.NewLine);

			return NT_STATUS.OK;

		}

		public override NT_STATUS Unlock(UserContext UserContext, FileContext fileContext, long Offset, long Count)
		{
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" Unlock"+Environment.NewLine);

			return NT_STATUS.OK;

		}

		public override NT_STATUS Read(UserContext UserContext, FileContext fileContext, long Offset, ref int Count, ref byte[] Buffer, int Start)
		{
			// All locking issues are handled in the calling class, the only read collision that can occure are when then 
			// application access the same file 
			NT_STATUS error = NT_STATUS.OK;

			String keyname=fileContext.Name.Substring(1,fileContext.Name.Length-1);
			//File.AppendAllText("wins3fs-log.txt",_mappedPath+" "+keyname+" Read Offset "+Offset.ToString()+" count "+Count.ToString()+Environment.NewLine);

			//need to have encryption??? Make null perhaps
			//ObjectGetRequest request = new ObjectGetRequest(_mappedPath, keyname);
			//ObjectGetResponse response = ThreeService.ObjectGet(request);
			//response.StreamResponseToFile("c:\\"+keyname);

			//Console.WriteLine(response.StreamResponseToString());
			//File.AppendAllText("wins3fs-log.txt",response.StreamResponseToString());
			//IThreeSharp NewService = new ThreeSharpQuery(ThreeConfig);

			if (Offset==0) {
				using (ObjectGetRequest request = new ObjectGetRequest(_mappedPath, keyname.Replace(" ","%20"))) //no space allowed, make url friendly
				using (ObjectGetResponse response =ThreeService.ObjectGet(request))
				//using (ObjectGetResponse response = ThreeService.ObjectGet(request))
				{
					//here we get the entire file and save it to a temporary location.  It would be nice in the future to specify a range
					//Range:bytes=0-10485759
					//in the call.  But it looks like the ThreeSharp class doesn't support it
					response.StreamResponseToFile(System.IO.Path.GetTempPath()+keyname); //does not like to have the same name as in the ObjectGetRequest call
				}
				((S3FileContext)fileContext).FileStream=File.Open(System.IO.Path.GetTempPath()+keyname,FileMode.Open, FileAccess.Read, FileShare.Read);
			}
			S3FileContext S3Context = (S3FileContext)fileContext;

			/*if (S3Context.IsDirectory || S3Context.FileStream == null)
			{
				Debug.WriteLine("Warnung->Aus einem Verzeichnis kann nicht gelesen werden");
				Count = 0;
				return NT_STATUS.INVALID_HANDLE;						// ERROR_INVALID_HANDLE
			}

			if (!S3Context.FileStream.CanRead && !S3Context.FileStream.CanSeek)
			{
				Debug.WriteLine("Warnung->Das der Datei kann nicht gelesen werden.");
				Count = 0;
				return NT_STATUS.INVALID_PARAMETER;						// ERROR_INVALID_PARAMETER;
			}

			if (Count > 0x0FFFFFFF)
			{
				Debug.WriteLine("Warnung->Anzahl der zu lesenden Bytes ist zu groß.");
				Count = 0;
				return NT_STATUS.INVALID_PARAMETER;						//ERROR_INVALID_PARAMETER
			}
			*/
			
			long NewOffset;
			try
			{
				NewOffset = S3Context.FileStream.Seek(Offset, System.IO.SeekOrigin.Begin);
				if (NewOffset != Offset)
				{
					Debug.WriteLine("Warnung->Von der angegebenen Position kann nicht gelesen werden.");
					Count = 0;
					return NT_STATUS.INVALID_PARAMETER;                 // 132 = ERROR_SEEK_ON_DEVICE
				}

				BinaryReader Reader = new BinaryReader(S3Context.FileStream);

				Count = Reader.Read(Buffer, Start, Count);
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warnung->Exception bei Read: " + ex.Message);
				Count = 0;
				//error = 30;					// ERROR_READ_FAULT
				error = (NT_STATUS)Marshal.GetHRForException(ex);
			}
			
			if (S3Context.FileStream.Length==Offset+Count)
			File.AppendAllText("wins3fs-log.txt","done "+S3Context.FileStream.Length.ToString()+Environment.NewLine);
			
			return error;
		}

		public override NT_STATUS SetAttributes(UserContext UserContext, FileContext fileContext, DirectoryContext data)
		{
			// Be aware of the delete flag! Is often used to delete files. 
			NT_STATUS error = NT_STATUS.OK;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" SetAttributes"+Environment.NewLine);

			
			FileInfoEx FI;
			DirectoryInfo DI;

			S3FileContext S3Context = (S3FileContext)fileContext;

			try
			{
				if (S3Context.IsDirectory)
				{
					DI = new DirectoryInfo(_mappedPath + S3Context.Name);
					if ((int)data.Attrib != -1)
					DI.Attributes = data.Attrib | FileAttributes.Directory;
					if (data.CreationTime != DirectoryContext.DateTimeFileStart)
					DI.CreationTime = data.CreationTime;
					if (data.LastAccessTime != DirectoryContext.DateTimeFileStart)
					DI.LastAccessTime = data.LastAccessTime;
					if (data.LastWriteTime != DirectoryContext.DateTimeFileStart)
					DI.LastWriteTime = data.LastWriteTime;
				}
				else
				{
					FI = new FileInfoEx(_mappedPath + S3Context.Name);
					if ((int)data.Attrib != -1)
					FI.Attributes = data.Attrib;
					if (data.CreationTime != DirectoryContext.DateTimeFileStart)
					FI.CreationTime = data.CreationTime;
					if (data.LastAccessTime != DirectoryContext.DateTimeFileStart)
					FI.LastAccessTime = data.LastAccessTime;
					if (data.LastWriteTime != DirectoryContext.DateTimeFileStart)
					FI.LastWriteTime = data.LastWriteTime;

					if (data.FileSize != -1)
					{
						if (S3Context.FileStream != null && !S3Context.FileStream.CanWrite && !S3Context.FileStream.CanSeek)
						{
							Debug.WriteLine("Warnung->Die Länger der Datei kann nicht geschrieben werden.");
							return NT_STATUS.INVALID_PARAMETER;						// ERROR_INVALID_PARAMETER;
						}

						S3Context.FileStream.SetLength(data.FileSize);
					}
				}
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warning->Exception beim Setzen der FileAttribute: " + ex.Message);
				//error = 1;			// Something was going wrong
				error = (NT_STATUS)Marshal.GetHRForException(ex);
			}
			return error;
		}

		public override NT_STATUS GetAttributes(UserContext UserContext, FileContext fileContext, out DirectoryContext data)
		{
			// Should be implemented very fast !
			// Will be called very often, so aviod to make real IO to get the result or set up a cache e.g.
			// in S3FileContext, but be aware that for a file there could be many FIDs and therefore many FileContexts, so 
			// the cache must match to the same cache entry.
			FileInfoEx FI;

			S3FileContext S3Context = (S3FileContext)fileContext;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+"|"+fileContext.Name+" GetAttributes filecontext"+Environment.NewLine);
			
			data = new DirectoryContext();

			//if (S3Context.IsDirectory)
			//{
			//	File.AppendAllText("wins3fs-log.txt"," GetAttributes IsDirectory"+Environment.NewLine);
			//	DirectoryInfo DI = new DirectoryInfo(_mappedPath + S3Context.Name);
			//	data.Attrib = DI.Attributes;                            
			//	data.CreationTime = DI.CreationTime;
			//	data.LastAccessTime = DI.LastAccessTime;
			//	data.LastWriteTime = DI.LastWriteTime;
			//	data.FileSize = 0;
			//	data.Name = DI.Name;
			//	data.ShortName = GetShortName(_mappedPath + S3Context.Name);
			//	return NT_STATUS.OK;
			//}
			
			int localfile=0;
			try {
				FI = new FileInfoEx(System.IO.Path.GetTempPath() + S3Context.Name);
				File.AppendAllText("wins3fs-log.txt"," GetAttributes IsFile"+Environment.NewLine);
				data.Attrib = FI.Attributes;
				data.CreationTime = FI.CreationTime;
				data.LastAccessTime = FI.LastAccessTime;
				data.LastWriteTime = FI.LastWriteTime;
				data.FileSize = FI.Length;
				// data.AllocationSize will be set to the same value as FileSize, there is no easy managed way to provide the informaiton
				// so forget about it
				data.Name = FI.Name;
				data.ShortName = GetShortName(_mappedPath + S3Context.Name);
				FI = null;
				localfile=1;
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Warning->Beim Setzen der LastWriteTime für '" + S3Context.Name + "' ist in Close eine Exeption aufgetreten: " + ex.Message);
			}

			if (localfile==1) {

				return NT_STATUS.OK;
			}
			
			if (fileContext.Name.Length==1){
				//query s3 to get directory/bucket info;
				XmlNodeList elemList = objDocument.GetElementsByTagName("Name");
				XmlNodeList elemListdate = objDocument.GetElementsByTagName("CreationDate");
				
				//bool bucket_found=false;
				int bucket_found=-1;
				String bDate ="";
				String bTime="";
				for (int i=0; i < elemList.Count; i++)
				{ 
					if (elemList[i].InnerXml==_mappedPath) {
						bucket_found=i;
						//File.AppendAllText("wins3fs-log.txt",elemListdate[i].InnerXml+Environment.NewLine);
						bDate=elemListdate[i].InnerXml.Substring(5,2)+"/"+elemListdate[i].InnerXml.Substring(8,2)+"/"+elemListdate[i].InnerXml.Substring(0,4);
						bTime=elemListdate[i].InnerXml.Substring(11,2)+":"+elemListdate[i].InnerXml.Substring(14,2)+":"+elemListdate[i].InnerXml.Substring(17,2);

						//File.AppendAllText("wins3fs-log.txt",DateTime.Now+Environment.NewLine);
						//File.AppendAllText("wins3fs-log.txt",Convert.ToDateTime(bDate+" "+bTime)+Environment.NewLine);
						i = elemList.Count;
					}
				}
				
				//TODO
				//need to parse elemListdate[i] into a useable date structure
				
				if (bucket_found!=-1)
				{
					data.Attrib = FileAttributes.Directory; //| DI.Attributes;
					data.CreationTime = Convert.ToDateTime(bDate+" "+bTime); //DI.CreationTime;
					data.LastAccessTime = data.CreationTime;
					data.LastWriteTime = data.CreationTime;
					data.FileSize = 0;
					data.Name = _mappedPath;
					data.ShortName = GetShortName(_mappedPath);
					//DI = null;
					return NT_STATUS.OK;
				}
			} else {
				File.AppendAllText("wins3fs-log.txt","GetAttributes single file "+fileContext.Name.Substring(1,fileContext.Name.Length-1)+Environment.NewLine);
				//XmlDocument contentDocument = new XmlDocument();
				//BucketListRequest request = new BucketListRequest(_mappedPath);
				//BucketListResponse response = ThreeService.BucketList(request);

				//contentDocument.LoadXml(response.StreamResponseToString());
				//contentDocument.LoadXml(wrapper.ListBucket(_mappedPath));
				XmlNodeList ContentsKey = contentDocument.GetElementsByTagName("Key");
				XmlNodeList ContentsDate = contentDocument.GetElementsByTagName("LastModified");
				XmlNodeList ContentsSize = contentDocument.GetElementsByTagName("Size");
				//int bucket_found=0;
				
				for (int i=0; i < ContentsKey.Count; i++)
				{ 
					if (ContentsKey[i].InnerXml==fileContext.Name.Substring(1,fileContext.Name.Length-1)) {
						//File.AppendAllText("wins3fs-log.txt",ContentsKey[i].InnerXml+" "+ContentsSize[i].InnerXml+" "+ContentsDate[i].InnerXml+Environment.NewLine);
						data.Attrib = FileAttributes.Normal; //| DI.Attributes;
						String bDate=ContentsDate[i].InnerXml.Substring(5,2)+"/"+ContentsDate[i].InnerXml.Substring(8,2)+"/"+ContentsDate[i].InnerXml.Substring(0,4);
						String bTime=ContentsDate[i].InnerXml.Substring(11,2)+":"+ContentsDate[i].InnerXml.Substring(14,2)+":"+ContentsDate[i].InnerXml.Substring(17,2);
						data.CreationTime = Convert.ToDateTime(bDate+" "+bTime); //Convert.ToDateTime(ContentsDate[i].InnerXml);
						data.LastAccessTime = data.CreationTime;
						data.LastWriteTime = data.CreationTime;
						data.FileSize = Convert.ToInt32(ContentsSize[i].InnerXml);
						data.Name = ContentsKey[i].InnerXml;
						data.ShortName = GetShortName(ContentsKey[i].InnerXml);
						return NT_STATUS.OK;
					}
				}
				//return NT_STATUS.OK;
			}
			
			return NT_STATUS.OBJECT_PATH_NOT_FOUND;
		}

		public override NT_STATUS GetAttributes(UserContext UserContext, string PathName, out DirectoryContext data) //, SearchFlag SF)
		{
			//gets information about the list of buckets or the items in the bucket itself.
			//
			// Will not be called very often, so an idea could be to call the sequecene: Create, GetAttributes and Close
			string FileName = _mappedPath + PathName;
			data = new DirectoryContext();
			
			File.AppendAllText("wins3fs-log.txt",_mappedPath+" GetAttributes string "+PathName+Environment.NewLine);
			
			if (PathName.Length==1){
				//query s3 to get directory/bucket info;
				XmlNodeList elemList = objDocument.GetElementsByTagName("Name");
				XmlNodeList elemListdate = objDocument.GetElementsByTagName("CreationDate");
				
				//bool bucket_found=false;
				int bucket_found=-1;
				String bDate ="";
				String bTime="";
				for (int i=0; i < elemList.Count; i++)
				{ 
					if (elemList[i].InnerXml==_mappedPath) {
						bucket_found=i;
						//File.AppendAllText("wins3fs-log.txt",elemListdate[i].InnerXml+Environment.NewLine);
						bDate=elemListdate[i].InnerXml.Substring(5,2)+"/"+elemListdate[i].InnerXml.Substring(8,2)+"/"+elemListdate[i].InnerXml.Substring(0,4);
						bTime=elemListdate[i].InnerXml.Substring(11,2)+":"+elemListdate[i].InnerXml.Substring(14,2)+":"+elemListdate[i].InnerXml.Substring(17,2);

						//File.AppendAllText("wins3fs-log.txt",DateTime.Now+Environment.NewLine);
						//File.AppendAllText("wins3fs-log.txt",Convert.ToDateTime(bDate+" "+bTime)+Environment.NewLine);
						i = elemList.Count;
					}
				}
				
				//TODO
				//need to parse elemListdate[i] into a useable date structure
				
				if (bucket_found!=-1)
				{
					data.Attrib = FileAttributes.Directory; //| DI.Attributes;
					data.CreationTime = Convert.ToDateTime(bDate+" "+bTime); //DI.CreationTime;
					data.LastAccessTime = data.CreationTime;
					data.LastWriteTime = data.CreationTime;
					data.FileSize = 0;
					data.Name = _mappedPath;
					data.ShortName = GetShortName(_mappedPath);
					//DI = null;
					return NT_STATUS.OK;
				}
			} else {
				File.AppendAllText("wins3fs-log.txt","GetAttributes single file "+PathName.Substring(1,PathName.Length-1)+Environment.NewLine);
				//XmlDocument contentDocument = new XmlDocument();
				//BucketListRequest request = new BucketListRequest(_mappedPath);
				//BucketListResponse response = ThreeService.BucketList(request);

				//contentDocument.LoadXml(response.StreamResponseToString());
				//contentDocument.LoadXml(wrapper.ListBucket(_mappedPath));
				XmlNodeList ContentsKey = contentDocument.GetElementsByTagName("Key");
				XmlNodeList ContentsDate = contentDocument.GetElementsByTagName("LastModified");
				XmlNodeList ContentsSize = contentDocument.GetElementsByTagName("Size");
				//int bucket_found=0;
				
				for (int i=0; i < ContentsKey.Count; i++)
				{ 
					if (ContentsKey[i].InnerXml==PathName.Substring(1,PathName.Length-1)) {
						//File.AppendAllText("wins3fs-log.txt",ContentsKey[i].InnerXml+" "+ContentsSize[i].InnerXml+" "+ContentsDate[i].InnerXml+Environment.NewLine);
						data.Attrib = FileAttributes.Normal; //| DI.Attributes;
						String bDate=ContentsDate[i].InnerXml.Substring(5,2)+"/"+ContentsDate[i].InnerXml.Substring(8,2)+"/"+ContentsDate[i].InnerXml.Substring(0,4);
						String bTime=ContentsDate[i].InnerXml.Substring(11,2)+":"+ContentsDate[i].InnerXml.Substring(14,2)+":"+ContentsDate[i].InnerXml.Substring(17,2);
						data.CreationTime = Convert.ToDateTime(bDate+" "+bTime); //Convert.ToDateTime(ContentsDate[i].InnerXml);
						data.LastAccessTime = data.CreationTime;
						data.LastWriteTime = data.CreationTime;
						data.FileSize = Convert.ToInt32(ContentsSize[i].InnerXml);
						data.Name = ContentsKey[i].InnerXml;
						data.ShortName = GetShortName(ContentsKey[i].InnerXml);
						return NT_STATUS.OK;
					}
				}
				//return NT_STATUS.OK;
			}
			

			return NT_STATUS.OBJECT_PATH_NOT_FOUND;
			
		}

		public override NT_STATUS GetStreamInfo(UserContext UserContext, string Name, out List<DirectoryContext> StreamInfo)
		{
			string FileName = _mappedPath + Name;

			File.AppendAllText("wins3fs-log.txt",_mappedPath+" GetStreamInfo"+Environment.NewLine);

			StreamInfo = new List<DirectoryContext>();
			DirectoryContext Stream;
			FileInfoEx FI;

			if (!FileEx.Exists(FileName))
			return NT_STATUS.OBJECT_NAME_NOT_FOUND;

			string[] StreamList = FileEx.StreamList(FileName);

			for (int i = 0; i<StreamList.Length; i++)
			{
				Stream = new DirectoryContext();
				Stream.Name = ":" + StreamList[i] + ":$DATA";

				if (StreamList[i] != string.Empty)
				FI = new FileInfoEx(FileName + ":" + StreamList[i]);
				else
				FI = new FileInfoEx(FileName);

				Stream.FileSize = FI.Length;
				StreamInfo.Add(Stream);
			}

			return NT_STATUS.OK;
		}

		private static string GetShortName(string LongName)
		{
			//File.AppendAllText("wins3fs-log.txt"," Shortname"+Environment.NewLine);
			
			System.Text.StringBuilder ShortName = new System.Text.StringBuilder(260);
			int result = GetShortPathName(LongName, ShortName, ShortName.Capacity);


			string Name = ShortName.ToString();
			if (result != 0)
			if (Name.IndexOf("\\") == -1)
			return Name;
			else
			return (Name.Substring(Name.LastIndexOf("\\") + 1)).ToUpper();

			int error = Marshal.GetLastWin32Error();
			return "Error.8_3";
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern int GetShortPathName([MarshalAs(UnmanagedType.LPTStr)]string lpszLongPath, [MarshalAs(UnmanagedType.LPTStr)] System.Text.StringBuilder lpszShortPath, int cchBuffer);

		private bool disposed = false;

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!this.disposed)
			{
				if (disposing)
				{
					// Dispose managed resources here.
				}

				// Dispose unmanaged resources here.

				// Nothing to do in this case. If you have to do here something, make it fast!
			}
			disposed = true;
		}

		~S3FS()
		{
			Dispose(false);
		}

	}	// S3FS
}	//Namespace

