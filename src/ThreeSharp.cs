using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.IO;
using System.Xml;
using System.Collections;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualBasic;

using Affirma.ThreeSharp.Model;
using Affirma.ThreeSharp.Query;
using Affirma.ThreeSharp.Wrapper;


namespace Affirma.ThreeSharp.ConsoleSample
{
	class ThreeSharpConsoleSample
	{
		static void Main(string[] args)
		{
			try
			{
				//if (awsAccessKeyId.StartsWith("<INSERT") || awsSecretAccessKey.StartsWith("<INSERT"))
				//throw new Exception("You must edit the code and insert your access keys to run the samples.");
				//
				//TODO add a command line switch to allow new ids to be entered
				
				int result;
				while((result=ThreeSharpConfig())!=0) {
					if (result<0) return; //config failed.  Most likely invaild awskeys
					//if result==1 then we re run config.
				}
				
				string service_name="WinFUSE";
				ServiceController[] scServices;
				scServices = ServiceController.GetServices();
				int found_winfuse_service=0;
				
				foreach (ServiceController scTemp in scServices)
				{

					if (scTemp.ServiceName == service_name)
					{
						found_winfuse_service=1;
						ServiceController sc = new ServiceController(service_name);
						sc.Stop();
						while (sc.Status != ServiceControllerStatus.Stopped)
						{
							Thread.Sleep(1000);
							sc.Refresh();
						}
						sc.Start();
						while (sc.Status == ServiceControllerStatus.Stopped)
						{
							Thread.Sleep(1000);
							sc.Refresh();
						}
						/*				if (sc.Status == ServiceControllerStatus.Stopped)
					{
						sc.Start();
						while (sc.Status == ServiceControllerStatus.Stopped)
						{
							Thread.Sleep(1000);
							sc.Refresh();
						}
					}
	*/
					}
				}
				
				if (found_winfuse_service==0) {
					//stop the running winfuse process
					Process[] myProcesses;
					//Returns array containing all instances of Notepad.
					myProcesses = Process.GetProcessesByName("winfuse");
					foreach (Process myProcess in myProcesses)
					{
						myProcess.Kill();
					}
					Process.Start("winfuse.exe");
				}
				/*Process p = new Process();
				p.StartInfo.WorkingDirectory = "c:\\";
				p.StartInfo.Arguments = @"stop WinFUSE";
				p.StartInfo.FileName = @"net.exe";
				p.Start();
				p.StartInfo.Arguments = @"start WinFUSE";
				p.StartInfo.FileName = @"net.exe";
				p.Start();
				*/
			}
			catch (ThreeSharpException ex)
			{
				StringBuilder sb = new StringBuilder();
				sb.AppendLine("An exception occurred.");
				sb.AppendLine("Message: " + ex.Message);
				sb.AppendLine("HTTP Status Code: " + ex.StatusCode.ToString());
				sb.AppendLine("Error Code: " + ex.ErrorCode);
				sb.AppendLine("XML: " + ex.XML);
				Console.WriteLine(sb.ToString());
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
		}

		static int ThreeSharpConfig()
		{
			// Convert the bucket name to lowercase for vanity domains.
			// the bucket must be lower case since DNS is case-insensitive.
			// returns -1 if the aws keys are not vaild after several attempts to query the user
			// returns 0 if configuration was successful
			// returns 1 if configureation needs to be run again after adding a bucket.
			
			String OutputString="";
			String OutputTemplate="";
			int Template_once=0;

			StreamReader sr = File.OpenText("WinFUSE.exe.conf-template"); 
			String s="";
			Regex rx = new Regex("awsAccessKeyId");
			while ((s = sr.ReadLine()) != null) 
			{
				if(rx.IsMatch(s)){
					//need to extract awsAccessKeyId, awsSecretAccessKey
					//string aws_ids[]=s.Split(' ');
					//Regex.Match(s,"awsAccessKeyId");
					//Regex awsrx = new Regex("awsAccessKeyId");
					
					string awsAccessKeyId = "";
					string awsSecretAccessKey = "";

					int aws_index;

					if((aws_index=s.IndexOf("\"",s.IndexOf("awsAccessKeyId")))>0) {
						if (s.Length>aws_index+21) {
						awsAccessKeyId=s.Substring(aws_index+1,20); //need to check that there is text available
						}
					};

					if((aws_index=s.IndexOf("\"",s.IndexOf("awsSecretAccessKey")))>0) {
						if (s.Length>aws_index+41) {
						awsSecretAccessKey=s.Substring(aws_index+1,40); //need to check that there is text available
						}
					};
					
					
					//see if we can access the list of buckets, thereby cheching the awsAccess keys provided
					int list_bucket_success=0;
					ThreeSharpWrapper wrapper = new ThreeSharpWrapper(awsAccessKeyId, awsSecretAccessKey); 
					String allBuckets="";
					while (list_bucket_success<2) {  //let the use try to enter valid it two times
						try {
							allBuckets=wrapper.ListBucket(null);
							list_bucket_success=10;
						}
						catch {
							//MessageBox.Show("Provide vaild awsAccessKeyId numbers", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
							awsAccessKeyId = Interaction.InputBox("Please enter a valid awsAccessKeyId","Bucket","awsAccessKeyId",100,100);
							awsSecretAccessKey = Interaction.InputBox("Please enter a valid awsSecretAccessKey","Bucket","awsSecretAccessKey",100,100);
							wrapper = new ThreeSharpWrapper(awsAccessKeyId, awsSecretAccessKey); 
							list_bucket_success++;
						}
					}
					
					if (list_bucket_success!=10) {
						MessageBox.Show("Config failed", "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return -1;
					}
					
					//rebuild the template so we don't have to ask for the aws ids every time.
					if (Template_once==0) {
						OutputTemplate+="\t\t<add name=\"username\" awsAccessKeyId=\""+awsAccessKeyId+"\" awsSecretAccessKey=\""+awsSecretAccessKey+"\" />\n";
						Template_once=1;
					}
					String Name="";
					
					
					//prase the returned bucket list from ListBucket() and make a list of available buckets
					XmlDocument objDocument = new XmlDocument();
					objDocument.LoadXml(allBuckets);
					
					XmlNodeList elemList = objDocument.GetElementsByTagName("DisplayName");
					if (elemList.Count==0) {
						//have user enter a default bucket if none is present
						Random r = new Random();
						string BucketName = Interaction.InputBox("Please enter a bucketname\n and rerun the config program","Bucket","default"+r.Next(50000).ToString(),100,100);
						wrapper.AddBucket(BucketName);
						return 1; //rerun config
					}
					for (int i=0; i < elemList.Count; i++)
					{   
						// Display all book titles in the Node List.
						//Console.WriteLine(elemList[i].InnerXml);
						Name=elemList[i].InnerXml;
					}
					//Console.WriteLine();

					elemList = objDocument.GetElementsByTagName("Name");
					for (int i=0; i < elemList.Count; i++)
					{   
						//Console.WriteLine("\t<add name=\""+elemList[i].InnerXml+"\" description=\"S3 "+elemList[i].InnerXml+"\" type=\"Suchwerk.FileSystem.S3FS, S3FS\" mappedpath=\""+elemList[i].InnerXml+"\" awsAccessKeyId=\""+awsAccessKeyId+"\" awsSecretAccessKey=\""+awsSecretAccessKey+"\" />");
						OutputString+="\t\t<add name=\""+elemList[i].InnerXml+"\" description=\"S3 "+elemList[i].InnerXml+"\" type=\"Suchwerk.FileSystem.S3FS, S3FS\" mappedpath=\""+elemList[i].InnerXml+"\" awsAccessKeyId=\""+awsAccessKeyId+"\" awsSecretAccessKey=\""+awsSecretAccessKey+"\" />\n";
						// Display all book titles in the Node List.
						//Console.WriteLine(elemList[i].InnerXml);
					}
					//OutputString+="<change me>\n";
				} else {
					OutputString+=s+"\n";
					OutputTemplate+=s+"\n";
				}
			}
			sr.Close();
			
			OutputString+="\n";
			//OutputTemplate+="\n";
			
			//OutputString+=File.ReadAllText("WinFUSE.exe.config2");
			File.WriteAllText("WinFUSE.exe.config",OutputString);
			File.WriteAllText("WinFUSE.exe.conf-template",OutputTemplate);
			//WinFuse.Close();
			return 0; //config complete
		}
	}
}

