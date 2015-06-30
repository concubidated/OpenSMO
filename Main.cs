using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Collections;
using System.Reflection;

namespace OpenSMO
{
public class MainClass
{
	public TcpListener tcpListener;
	public TcpListener tcpListenerRTS;
	public int FPS = 120;
	public string userlist = "";
	public string lobbyusers = "";
	public List<User> Users = new List<User>();
	public List<Room> Rooms = new List<Room>();

	public byte ServerOffset = 128;
	public byte ServerVersion = 128;
	public byte ServerMaxPlayers = 255;

	public Config ServerConfig;
	public Scripting Scripting;

	public static int Build = 6;
	public static MainClass Instance;
	public static DateTime StartTime;

	public string builddate = RetrieveLinkerTimestamp().ToString("MM/dd/yy HH:mm:ss");

	void ShowHelp()
	{
		Console.WriteLine("Usage is: OpenSMO [options]");
		Console.WriteLine("Options:");
		Console.WriteLine("  -h            : Show this help");
		Console.WriteLine("  -v            : Show current version");
		Console.WriteLine("  -c <filename> : Load a specific config file");
	}
	public static DateTime RetrieveLinkerTimestamp()
	{
		string filePath = System.Reflection.Assembly.GetCallingAssembly().Location;
		const int c_PeHeaderOffset = 60;
		const int c_LinkerTimestampOffset = 8;
		byte[] b = new byte[2048];
		System.IO.Stream s = null;

		try
		{
			s = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
			s.Read(b, 0, 2048);
		}
		finally
		{
			if (s != null)
			{
				s.Close();
			}
		}

		int i = System.BitConverter.ToInt32(b, c_PeHeaderOffset);
		int secondsSince1970 = System.BitConverter.ToInt32(b, i + c_LinkerTimestampOffset);
		DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0);
		dt = dt.AddSeconds(secondsSince1970);
		dt = dt.AddHours(TimeZone.CurrentTimeZone.GetUtcOffset(dt).Hours);
		return dt;
	}

	public MainClass(string[] args)
	{
		Instance = this;
		StartTime = DateTime.Now;

		string argConfigFile = "Config.ini";

		try
		{
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
				case "--help":
				case "-h":
				case "-?":
				default:
					this.ShowHelp();
					return;

				case "--version":
				case "-v":
					Console.WriteLine("OpenSMO build " + Build);
					return;

				case "--config":
				case "-c":
					argConfigFile = args[++i];
					break;
				}
			}
		}
		catch
		{
			this.ShowHelp();
		}

		ServerConfig = new Config(argConfigFile);

		Console.Title = ServerConfig.Get("Server_Name");
		AddLog("Server starting at " + StartTime + " Build Date: " + builddate);
		if (bool.Parse(ServerConfig.Get("Server_HigherPriority")))
		{
			AddLog("Setting priority to above normal");
			Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
		}

		FPS = int.Parse(ServerConfig.Get("Server_FPS"));

		// Get optional advanced settings
		if (ServerConfig.Contains("Server_Offset")) ServerOffset = (byte)int.Parse(ServerConfig.Get("Server_Offset"));
		if (ServerConfig.Contains("Server_Version")) ServerVersion = (byte)int.Parse(ServerConfig.Get("Server_Version"));
		if (ServerConfig.Contains("Server_MaxPlayers")) ServerMaxPlayers = (byte)int.Parse(ServerConfig.Get("Server_MaxPlayers"));

		MySql.Host = ServerConfig.Get("MySql_Host");
		MySql.User = ServerConfig.Get("MySql_User");
		MySql.Password = ServerConfig.Get("MySql_Password");
		MySql.Database = ServerConfig.Get("MySql_Database");
		MySql.Timeout = ServerConfig.Get("MySql_Timeout");

		ReloadScripts();

		tcpListener = new TcpListener(IPAddress.Parse(ServerConfig.Get("Server_IP")), int.Parse(ServerConfig.Get("Server_Port")));
		tcpListener.Start();

		AddLog("Server started on port " + ServerConfig.Get("Server_Port"));

		new Thread(new ThreadStart(UserThread)).Start();

		if (bool.Parse(ServerConfig.Get("RTS_Enabled")))
		{
			tcpListenerRTS = new TcpListener(IPAddress.Parse(ServerConfig.Get("RTS_IP")), int.Parse(ServerConfig.Get("RTS_Port")));
			tcpListenerRTS.Start();

			AddLog("RTS server started on port " + ServerConfig.Get("RTS_Port"));

			new Thread(new ThreadStart(RTSThread)).Start();
		}

		AddLog("Server running.");

		while (true)
		{
			TcpClient newTcpClient = tcpListener.AcceptTcpClient();

			string IP = "";
			try
			{
				IP = newTcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];
			}
			catch (SocketException e)
			{
				AddLog("Connection Exception: Disconnected: error code: " + e.NativeErrorCode.ToString());
				continue;
			}
			if (Data.IsBanned(IP))
			{
				if (bool.Parse(ServerConfig.Get("Game_ShadowBan")))
				{
					AddLog("Shadowbanned client connected: " + IP, true);

					User newUser = new User(this, newTcpClient);
					newUser.ShadowBanned = true;
					lock(Users)
					{
						Users.Add(newUser);
					}
				}
				else
				{
					AddLog("Banned client kicked: " + IP, true);
					newTcpClient.Close();
				}
			}
			else
			{
				AddLog("Client connected: " + IP);

				User newUser = new User(this, newTcpClient);
				lock(Users)
				{
					Users.Add(newUser);
				}
				lock(Users)
				{
					foreach (User user in Users)
					{
						if (user.CurrentRoom == null)
						{
							user.SendRoomPlayers();
						}
					}
				}
			}
		}
	}

	public void ReloadScripts()
	{
		Scripting = new Scripting();

		Scripting.Scope.SetVariable("main", this);
		Scripting.Scope.SetVariable("config", ServerConfig);

		Scripting.Start();
	}

	public long Epoch()
	{
		return (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
	}

	public void UserThread()
	{
		while (true)
		{

			try
			{
				for (int i = 0; i < Scripting.UpdateHooks.Count; i++)
				{
					Scripting.UpdateHooks[i]();
				}
			}
			catch (Exception ex)
			{
				Scripting.HandleError(ex);
			}
			lock(Users)
			{
				for (int i = 0; i < Users.Count; i++)
				{
					if (Users[i] != null)
					{
						Users[i].Update();
					}
				}
				for (int i = 0; i < Rooms.Count; i++)
				{
					if (Rooms[i] != null)
					{
						Rooms[i].Update();
					}
				}
			}

			Thread.Sleep(2);

//        Thread.Sleep(1000 / FPS);
		}
	}

	public static Random rnd = new Random();
	public static string RandomString(int len, string chars = "abcdefghijklmnopqrstuvwxyz0123456789")
	{
		string ret = "";
		for (int i = 0; i < len; i++)
		{
			string a = chars[rnd.Next(chars.Length)].ToString();
			if (rnd.Next(2) == 0)
				ret += a.ToUpper();
			else
				ret += a;
		}
		return ret;
	}

	public string JsonSafe(string str)
	{
		return OpenSMO.User.Utf8Decode(str).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
		//return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
	}

	public User[] GetUsersInLobby()
	{
		List<User> ret = new List<User>();
		List<User> users = GetAllUsers();
		foreach (User user in users)
		{
			if (user.CurrentRoom != null)
			{
			}
			else
			{
				ret.Add(user);
			}
		}
		return ret.ToArray();
	}

	public string GetLobbyUsers()
	{
		lobbyusers = "";
		List<User> users  = GetAllUsers();
		foreach (User user in users)
		{
			if (user.CurrentRoom == null)
			{
				string quoted = user.User_ID + ":" + user.User_Name + ",";
				lobbyusers += quoted;
			}
		}
		return lobbyusers;
	}

        public List<User> GetAllUsers()
        {
		lock(Users)
		{
			List<User> users  = new List<User>(Users);
			return users;
		}
	}

	public void RTSThread()
	{
		while (true)
		{
			TcpClient newClient = tcpListenerRTS.AcceptTcpClient();

			new Thread(new ThreadStart(delegate
			{
				string IP = "";
				TcpClient newTcpClient = newClient;
				try
				{
					IP = newTcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];
				}
				catch (SocketException e)
				{
					AddLog("RTS Exception: Disconnected: error code: " + e.NativeErrorCode.ToString());
				}

				NetworkStream stream = newTcpClient.GetStream();
				stream.ReadTimeout = 500;
				stream.WriteTimeout = 500;
				StreamReader reader = new StreamReader(stream);
				StreamWriter writer = new StreamWriter(stream)
				{
					AutoFlush = true
				};

				try
				{
					string line = reader.ReadLine();
					if (line != null)
					{
						string[] requestParts = line.Split(' ')[1].Substring(1).Split(new char[] { '?' }, 2);
						string request = requestParts[0];
						string[] parse = request.Split('/');

						string roomID = "";
						Room r = null;


						string responseBuffer = "";
						switch (parse[0])
						{
						case "builddate":
							responseBuffer = builddate;
							break;
						case "uptime":
							responseBuffer = ((int)((DateTime.Now - StartTime).TotalMilliseconds / 1000d)).ToString();
							break;

						case "l":
							responseBuffer = "[";


							//Lobby

							responseBuffer += "[";
							responseBuffer += "\"Lobby\",";
							responseBuffer += "\"Lobby\",";
							responseBuffer += "\"\",";
							responseBuffer += "\"Admin\",";
							responseBuffer += GetUsersInLobby().Count().ToString() + ",";
							responseBuffer += "\"0\",";
							responseBuffer += "\"N/A\",";
							responseBuffer += "\"N/A\",";

							userlist = GetLobbyUsers();
							responseBuffer += "\"" + userlist.TrimEnd(',') + "\"";
							userlist = "";
							responseBuffer += "]";
							responseBuffer += ",";


							foreach (Room room in Rooms)
							{
								if (!room.Secret && !room.Owner.ShadowBanned)
								{
									string playerlist;
									responseBuffer += "[";
									responseBuffer += "\"" + room.ID + "\",";
									responseBuffer += "\"" + JsonSafe(room.Name) + "\",";
									responseBuffer += "\"" + JsonSafe(room.Description) + "\",";
									responseBuffer += "\"" + JsonSafe(room.Owner.User_Name) + "\",";
									responseBuffer += room.Users.Count + ",";
									responseBuffer += "\"" + room.Status.ToString() + "\",";
									responseBuffer += "\"" + JsonSafe(room.CurrentSong.Name) + "\",";
									responseBuffer += "\"" + JsonSafe(room.CurrentSong.Artist) + "\",";

									userlist = room.GetRoomUsers();
									responseBuffer += "\"" + userlist.TrimEnd(',') + "\"";
									userlist = "";
									responseBuffer += "]";

									if (Rooms.Last() != room)
										responseBuffer += ",";
								}
							}
							responseBuffer += "]";
							break;

//		case "a":
//			responseBuffer = "[";
//			User[] serverusers = User.GetUsersInServer();
//			{
//				foreach (User user in serverusers)
//				{
//					responseBuffer += "[";
//					responseBuffer += "\"" + user.User_Name + "\",";
//					responseBuffer += "],";
//				}
//			}
//			responseBuffer += "]";
//			break;


						case "g":
							roomID = parse[1];
							r = null;
							foreach (Room room in Rooms)
							{
								if (room.ID == roomID)
								{
									r = room;
									break;
								}
							}

							if (r == null || r.Secret)
							{
								responseBuffer = "[]";
							}
							else
							{
								User[] usersOrig = r.Users.ToArray();
								if (usersOrig.Length == 0)
								{
									responseBuffer = "[]";
								}
								else
								{
									User[] users = (from user in usersOrig orderby user.SMOScore descending select user).ToArray();
									responseBuffer += "[[\"" + JsonSafe(r.Name) + "\",";
									responseBuffer += "\"" + JsonSafe(r.Description) + "\",";
									responseBuffer += "\"" + JsonSafe(r.CurrentSong.Artist + " - " + r.CurrentSong.Name) + "\",";
									if (r.CurrentSong.Time == 0)
										responseBuffer += "false,";
									else
										responseBuffer += "\"" + (int)Math.Min(100, Math.Floor(100d / r.CurrentSong.Time * usersOrig[0].SongTime.ElapsedMilliseconds / 1000d)) + "%\",";
									responseBuffer += "\"" + JsonSafe(r.ChatBuffer) + "\"";
									responseBuffer += "],";
									foreach (User user in users)
									{
										responseBuffer += "[";
										responseBuffer += user.User_ID + ",";
										responseBuffer += "\"" + JsonSafe(user.User_Name) + "\",";
										responseBuffer += "\"" + user.Combo + "\",";
										responseBuffer += user.SMOScore + ",";
										responseBuffer += "\"" + user.Grade + "\",";
										responseBuffer += "\"" + user.GameDifficulty + "\",";
										responseBuffer += "\"" + JsonSafe(user.GamePlayerSettings) + "\",";
										responseBuffer += "\"" + JsonSafe(user.GamePlayerSettings) + "\",";
										if (user.Notes == null)
										{
											for (int i = 0; i < 9; i++ )
											{
												responseBuffer += "\"0\",";
											}
										}
										else
										{
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Flawless].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Perfect].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Great].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Good].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Barely].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Miss].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.Held].ToString()) + "\",";
											responseBuffer += "\"" + JsonSafe(user.Notes[(int)NSNotes.NG].ToString()) + "\",";
										}
										responseBuffer += "\"" + user.MaxCombo + "\",";
										responseBuffer += "\"" + user.percent + "\"";
										responseBuffer += "],";
									}
									responseBuffer += "]";
								}
							}
							break;

						case "c":
							if (IP != ServerConfig.Get("RTS_Trusted"))
							{
								responseBuffer = "[]";
								break;
							}

							roomID = parse[1];
							string webuserid = parse[2];
							string chatcolor = parse[3];
							string data = Uri.UnescapeDataString(parse[4]);
							//string data = requestParts.Length == 3 ? Uri.UnescapeDataString(requestParts[3]);
							Hashtable[] userRes = MySql.Query("select Username from users where id = " + webuserid + "");
							if (userRes.Length != 1)
							{
								break;
						 	}

							Hashtable u = userRes[0];

							r = null;
							lock(Rooms)
							{
								foreach (Room room in Rooms)
								{
									if (room.ID == roomID)
									{
										r = room;
										break;
									}
								}
							
							
								AddLog("WebChat from " + u["Username"].ToString() + ": " + data);
								if (r != null && !r.Secret) 
								{
									if (r.NoWebChat)
									{
										responseBuffer = "Web Chat Disabled on Room by Owner/OP";
										break;
									}
									string strName = u["Username"].ToString();
		
									SendChatAll(Func.ChatColor(chatcolor) +strName + Func.ChatColor("ffffff") + ": " + User.Utf8Encode(data), r);
									}
							}
		
							responseBuffer = "OK";
							break;
								
						}
						writer.WriteLine("HTTP/1.1 200 OK");
						writer.WriteLine("Content-Type: text/plain");
						writer.WriteLine("access-control-allow-origin: *");
						writer.WriteLine("access-control-allow-credentials: true");
						//Breaks incorrectly calculated utf8
						//writer.WriteLine("Content-Length: " + responseBuffer.Length);
						writer.WriteLine("Connection: close");
						writer.WriteLine();
						writer.Write(responseBuffer);
					}
				}
				catch (Exception ex)
				{
					AddLog("RTS request encountered '" + ex.GetType().Name + "' from " + IP, true);
				}

				stream.Close();
				stream.Dispose();
			})).Start();
		}
	}

	public static string Spaces(string input, int spaceCount)
	{
		string ret = "";
		for (int i = 0; i < spaceCount - input.Length; i++)
			ret += ' ';
		return ret + input;
	}

	public static void AddLog(string Str, bool Bad = false)
	{
		if (Bad) Console.ForegroundColor = ConsoleColor.Red;
		string line = "[" + Spaces(((DateTime.Now - StartTime).TotalMilliseconds / 1000d).ToString("0.000000").Replace(',', '.'), 14) + "] " + Str;
		Console.WriteLine(line);
		if (Bad) Console.ForegroundColor = ConsoleColor.Gray;

//      string logFilename = Instance.ServerConfig.Get("Server_LogFile");
//      if (logFilename != "") {
//        StreamWriter writer;
//        if (File.Exists(logFilename))
//          writer = File.AppendText(logFilename);
//        else
//          writer = new StreamWriter(File.Create(logFilename));
//
//        writer.WriteLine(line);
//        writer.Close();
//      }
	}


	public void SendChatAll(string Message)
	{
		List<User> userlist  = GetAllUsers();
		foreach (User user in userlist)
			user.SendChatMessage(Message);
	}

	public void SendChatAll(string Message, Room room)
	{
		if (room != null)
			room.AddChatBuffer(Message);

		for (int i = 0; i < Users.Count; i++)
		{
			User user = Users[i];
			if (user.CurrentRoom == room)
				user.SendChatMessage(Message);
		}
	}

	public void SendChatAll(string Message, Room room, User exception)
	{
		lock(Users)
		{
			foreach (User user in Users)
			{
				if (user.CurrentRoom == room && user != exception)
					user.SendChatMessage(Message);
			}
		}
	}

	public static void Main(string[] args)
	{
		new MainClass(args);
	}

	public static string MD5(string input)
	{
		byte[] hashBytes = System.Security.Cryptography.MD5.Create().ComputeHash(Encoding.ASCII.GetBytes(input));

		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < hashBytes.Length; i++)
			sb.Append(hashBytes[i].ToString("x2"));

		return sb.ToString().ToUpper();
	}
}
}
