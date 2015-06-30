using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace OpenSMO
{
public enum UserRank : int { User, Moderator, Admin }
public enum RoomRights : int { Player, Operator, Owner }

public class SyncStart
{
	public User mUser;
	public MainClass mainClass;
	public SyncStart(User user)
	{
		mUser = user;
	}

	public void StartWhenAllSynced(User[] checkSyncPlayers)
	{
		bool allwaiting = true;
		Stopwatch timeout = new Stopwatch();
		timeout.Restart();
		while(true)
		{
			if (mUser.skippacket)
			{
				break;
			}
			//MainClass.AddLog("Waited: " + timeout.ElapsedMilliseconds.ToString());

			lock(mUser)
			{
				lock(checkSyncPlayers)
				{
					foreach (User user in checkSyncPlayers)
					{
						if (!user.waiting)
						{
							allwaiting = false;
						}
					}
				}
			}
			
			if((timeout.ElapsedMilliseconds > 15000) || allwaiting)
			{
				if(timeout.ElapsedMilliseconds > 15000)
				{
					lock(mUser)
					{
						lock(checkSyncPlayers)
						{
							foreach (User user in checkSyncPlayers)
							{
								if (!user.waiting)
								{
									MainClass.AddLog("Gave up on waiting for:" + user.User_Name + ". Starting round");
								}
							}
						}
					}
				}
				// Start Playing
				lock(mUser)
				{
					lock(checkSyncPlayers)
					{
						foreach (User user in checkSyncPlayers)
						{
							MainClass.AddLog("Sent Song Start to " + user.User_Name + " which waited " + user.waittime.ElapsedMilliseconds.ToString() + "ms to start");
							user.waiting = false;
							user.skippacket = true;
							user.SendSongStartTo(checkSyncPlayers);
						}
						mUser.CurrentRoom.roomid = Data.CreateRoomDB(mUser);
						break;
					}
				}
			}
			// Only check every 100ms to reduce load and duplicate packet odds
			Thread.Sleep(100);
		}
		Thread.CurrentThread.Abort();
	}
};

public class SongScanner
{
	private User mUser;
	private Hashtable[] mSongs;
	private bool mScanning;
	private int mCurrentSongIndex;
	private string mCurrentPackName;
	private int mCurrentPackID;
	private int stopscan = 0;
	public MainClass mainClass;
	public Stopwatch loadtime = new Stopwatch();
	public Stopwatch scanlatency = new Stopwatch();
	public SongScanner(User user)
	{
		mUser = user;
		mScanning = false;
	}

	public bool IsScanning()
	{
		return mScanning;
	}
	public void StopScan()
	{
		stopscan = 1;
	}

	public string GetCurrentPackName()
	{
		return mCurrentPackName;
	}
	public int GetCurrentPackID()
	{
		return mCurrentPackID;
	}

	public void LoadSongs(Hashtable[] songs)
	{
		loadtime.Restart();
		mSongs = songs;
		mCurrentSongIndex = 0;
	}

	public bool SendNextSong()
	{
		int latency = (int)scanlatency.ElapsedMilliseconds;
		//MainClass.AddLog("Scan Latency is " + latency.ToString() + "ms");
		scanlatency.Restart();
		if ((mCurrentSongIndex >= mSongs.Count()) || (stopscan == 1))
		{
			stopscan = 0;
			mScanning = false;
			mUser.Scanning = false;
			mUser.scandone();
		}
		else
		{
			if ( (mCurrentSongIndex != 0) && (mCurrentSongIndex % 100) == 0)
			{
				int current = mSongs.Count();
				int ms = (int)loadtime.ElapsedMilliseconds;
				float scanpercf;
				float etaf;
				scanpercf =  ((float)mCurrentSongIndex/(float)current)*100F;
				string scanperc = scanpercf.ToString("n2");
				etaf = ( (100F/scanpercf) * ( (float)ms / 1000F ) - ( (float)ms / 1000F) );
				string eta =  etaf.ToString("n1");
				//string blah = "Total: "+current.ToString()+" Done: "+mCurrentSongIndex.ToString()+" Elapsed: "+ms.ToString();
				//mUser.sendlog(blah);
				mUser.scanpercent(scanperc, eta);
			}

			string title, subtitle, artist, titletrans, subtitletrans, artisttrans;
			string titles, subtitles, artists;
			Hashtable song = mSongs[mCurrentSongIndex];
			title = (string)song["title"];
			subtitle = (string)song["subtitle"];
			artist	= (string)song["artist"];
			titletrans = (string)song["titletrans"];
			subtitletrans = (string)song["subtitletrans"];
			artisttrans  = (string)song["artisttrans"];

			if (String.IsNullOrEmpty(titletrans))
			{
				titles = title;
			}
			else
			{
				titles = titletrans;
			}

			if (String.IsNullOrEmpty(subtitletrans))
			{
				subtitles = subtitle;
			}
			else
			{
				subtitles = subtitletrans;
			}
			if (String.IsNullOrEmpty(artisttrans))
			{
				artists = artist;
			}
			else
			{
				artists = artisttrans;
			}

			mCurrentPackName = (string)song["packname"];
			mCurrentPackID = (int)song["packid"];

			mUser.ScanSong(titles, subtitles, artists);
			++mCurrentSongIndex;
			mScanning = true;
		}

		return mScanning;
	}
}

public class User
{
	CultureInfo ci;
	public MainClass mainClass;
	public bool Connected = true;
	public bool ShadowBanned = false;

	public bool newjoin = false;
	public int User_ID = 0;
	public int User_Level_Rank = 0;
	public string User_Name = "";
	public string User_IP = "";
	public UserRank User_Rank = UserRank.User;
	public Hashtable User_Table = null;
	public Hashtable Rank_Table = null;
	public List<int> ignored = new List<int>();
	public string joinpass = "";
	public int nihongo = 0;
	public bool waiting = false;
	public bool gotstart = false;
        public bool skippacket = false;
	public int User_Protocol = 0;
	public string User_Game = "";
	public int connectioncount = 0;
	private Room _CurrentRoom = null;
	private SongScanner mSongScanner = null;
	private SyncStart mSyncStart = null;
	private int latency = 0;
	public Stopwatch latencyclock = new Stopwatch();
	public Stopwatch SelectTime = new Stopwatch();
	public Stopwatch waittime = new Stopwatch();
	public Stopwatch disconnectedsince = new Stopwatch();

	public Room CurrentRoom
	{
		get
		{
			return _CurrentRoom;
		}
		set
		{
			Room oldRoom = _CurrentRoom;
			_CurrentRoom = value;

			User[] lobbyUsers = GetUsersInRoom();

			if (value == null)
			{
				if (oldRoom == null) return;

				lock(mainClass.Users)
				{
					User[] users = oldRoom.Users.ToArray();
					if (users.Length == 0)
					{
						MainClass.AddLog("Removing room '" + oldRoom.Name + "'");
						mainClass.Rooms.Remove(oldRoom);
	
						foreach (User user in lobbyUsers)
							user.SendRoomList();
					}
					else
					{
						if (oldRoom.AllPlaying)
						{
							bool shouldStart = true;
							foreach (User user in users)
							{
								if (!user.Synced)
								{
									shouldStart = false;
									break;
								}
							}
	
							if (shouldStart)
							{
								SendSongStartTo(users);
								oldRoom.AllPlaying = false;
							}
						}
						mainClass.SendChatAll(NameFormat() + " left the room.", oldRoom, this);
	
						foreach (User user in users)
							user.SendRoomPlayers();
	
						if (users.Length > 0)
						{
							if (CurrentRoomRights == RoomRights.Owner)
							{
								User newOwner;
								int tmout = 0;
								do
								{
									newOwner = users[MainClass.rnd.Next(users.Length)];
									if (++tmout == 15) return;
								}
								while (newOwner == this);
								newOwner.CurrentRoomRights = RoomRights.Owner;
								newOwner.CurrentRoom.Owner = newOwner;
								mainClass.SendChatAll(newOwner.NameFormat() + " is now room owner.", newOwner.CurrentRoom);
							}
						}
						else
						{
							MainClass.AddLog("Removing room '" + oldRoom.Name + "'");
							mainClass.Rooms.Remove(oldRoom);
							oldRoom = null;
						}
						CurrentRoomRights = RoomRights.Player;
					}
					foreach (User user in lobbyUsers)
						user.SendRoomPlayers();
				}
			}
		}
	}
	public RoomRights CurrentRoomRights = RoomRights.Player;
	public NSScreen CurrentScreen = NSScreen.Loading;

	public TcpClient tcpClient;
	public BinaryWriter tcpWriter;
	public BinaryReader tcpReader;

	public Ez ez;

	public bool CanPlay = true;
	public bool Scanning = false;
	public bool ScanResponse = false;
	public bool Spectating = false;
	public bool ShowOffset = false;
	public bool Synced = false;
	public bool SyncNeeded = false;
	public bool Playing = false;
	public int[] Notes;
	public int NoteCount
	{
		get
		{
			if (Notes == null) return 0;
			int ret = 0;
			for (int i = 3; i <= 8; i++)
				ret += Notes[i];
			return ret;
		}
	}

	public int Score = 0;
	public int Combo = 0;
	public int MaxCombo = 0;
	private NSGrades _Grade = 0;
	public NSGrades Grade
	{
		get
		{
			return _Grade;
		}
		set
		{
			if (value >= NSGrades.AAA && isAAAA)
			{
				_Grade = NSGrades.AAAA;
				return;
			}
			else if (value >= NSGrades.AA && isAAA)
			{
				_Grade = NSGrades.AAA;
				return;
			}
			else if (value >= NSGrades.A && FullCombo)
			{
				_Grade = NSGrades.AA;
				return;
			}


			if ( _Grade != NSGrades.F )
			{
				int marv = Notes[(int)NSNotes.Flawless];
				int perf = Notes[(int)NSNotes.Perfect];
				int grea = Notes[(int)NSNotes.Great];
				int good = Notes[(int)NSNotes.Good];
				int boo  = Notes[(int)NSNotes.Barely];
				int miss = Notes[(int)NSNotes.Miss];
				int ok   = Notes[(int)NSNotes.Held];
				int ng   = Notes[(int)NSNotes.NG];
				int pnt = (2 * (marv + perf)) + grea - (4 * boo) - (8 * miss) + (6 * ok);
				int maxpnt = 2 * (marv + perf + grea + good + boo + miss) + 6 * (ok + ng);
				if ( maxpnt != 0 )
				{
					float perc = ((100f / maxpnt) * pnt);
					if (isAAAA)
					{
						_Grade = NSGrades.AAAA;
						return;
					}
					else if (isAAA)
					{
						_Grade = NSGrades.AAA;
						return;
					}
					else if (perc >= 93)
					{
						_Grade = NSGrades.AA;
						return;
					}
					else if (perc >= 80)
					{
						_Grade = NSGrades.A;
						return;
					}
					else if (perc >= 65)
					{
						_Grade = NSGrades.B;
						return;
					}
					else if (perc >= 45)
					{
						_Grade = NSGrades.C;
						return;
					}
					else if (perc < 45)
					{
						_Grade = NSGrades.D;
						return;
					}
				}
				else
				{
					_Grade = NSGrades.AAAA;
				}
			}
			else
			{
				_Grade = NSGrades.F;
			}

//        _Grade = value;
		}
	}

	public NSNotes NoteHit = NSNotes.Miss;
	public double NoteOffset = 0d;
	public ushort NoteOffsetRaw = 0;
	public float Tpnt = 0;
	public float Tmaxpnt = 0;
	public float percentf = 0;
	public int timing = 0;
	public string Pack;
	public int jump = 0;
	public int jumpxp = 0;
	public int perfmarv = 0;
	public int toasty = 0;
	public int GameFeet = 0;
	public string percent = "";
	public int servcombo = 0;
	public double clientoffset = 0;
	public int offsetpos = 0;
	public int offsetneg = 0;
	public int clientoffsetcount = 0;
	public NSDifficulty GameDifficulty = NSDifficulty.Beginner;
	public string GamePlayerSettings = "";
	public string CourseTitle = "";
	public string SongOptions = "";
	public int PlayerRate = 100;
	public Hashtable Meta = new Hashtable();

	public Stopwatch PlayTime = new Stopwatch();
	public Stopwatch SongTime = new Stopwatch();

	public int SMOScore
	{
		get
		{
			if (Notes == null) return 0;
			int marv = Notes[(int)NSNotes.Flawless];
			int perf = Notes[(int)NSNotes.Perfect];
			int grea = Notes[(int)NSNotes.Great];
			int good = Notes[(int)NSNotes.Good];
			int boo  = Notes[(int)NSNotes.Barely];
			int miss = Notes[(int)NSNotes.Miss];
			int ok   = Notes[(int)NSNotes.Held];
			int ng   = Notes[(int)NSNotes.NG];
			Tpnt = (3 * marv) + (2 * perf) + grea - (4 * boo) - (8 * miss) + (6 * ok);
			int migsdp=(int)Tpnt;
			return migsdp;;
		}
	}
	public bool FullCombo
	{
		get
		{
			int badCount = 0;
			for (int i = 3; i <= 5; i++)
				badCount += Notes[i];
			return badCount == 0;
		}
	}

	public bool isAAA
	{
		get
		{
			int badCount = 0;
			for (int i = 3; i <= 6; i++)
				badCount += Notes[i];
			return badCount == 0;
		}
	}

	public bool isAAAA
	{
		get
		{
			int badCount = 0;
			for (int i = 3; i <= 7; i++)
				badCount += Notes[i];
			return badCount == 0;
		}
	}



	public User(MainClass mainClass, TcpClient tcpClient)
	{
		this.mainClass = mainClass;
		this.tcpClient = tcpClient;

		this.User_IP = tcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];

		NetworkStream stream = tcpClient.GetStream();
		stream.ReadTimeout = 500;
		stream.WriteTimeout = 500;
		this.tcpWriter = new BinaryWriter(stream);
		this.tcpReader = new BinaryReader(stream);

		ez = new Ez(this);
	}

	~User()
	{
		CurrentRoom = null;
	}

	public bool RequiresAuthentication()
	{
		if (User_Name == "")
		{
			Kick();
			return false;
		}
		return true;
	}

	public bool RequiresRoom()
	{
		if (CurrentRoom == null)
		{
			Kick();
			return false;
		}
		return false;
	}

	public void friendlist()
	{
		Hashtable[] checkforusers = MySql.Query("select * from friends where user = " + User_ID.ToString() + "");
		User[] allusersfriends = GetUsersInServer();
		if (checkforusers.Count() != 0 )
		{
			for (int i = 0; i < checkforusers.Count(); i++)
			{
				foreach (User user in allusersfriends)
				{
					if (user.User_ID == (int)checkforusers[i]["friend"])
					{
						string friendroom;
						if ( user.CurrentRoom == null)
						{
							friendroom = "Lobby";
						}
						else
						{
							friendroom = user.CurrentRoom.Name;
						}
						 mainClass.SendChatAll(Func.ChatColor("ffffff") + "'" + user.NameFormat() + "'" + Func.ChatColor("ffff00") + ":  " + "(" + friendroom + ")" + Func.ChatColor("ffffff"), user.CurrentRoom);
					}
				}
			}
		}
	}
	public void sendlog(string logdata)
	{
		MainClass.AddLog(logdata);
	}

	public void scanpercent(string scanperc, string eta)
	{
		SendChatMessage("Scan Progress for "+ NameFormat() +": " + scanperc +"% Completed. ETA: " + eta + "s Remaining");
	}

	public void scandone()
	{
		mainClass.SendChatAll("Scan Completed for " + NameFormat()+" ", CurrentRoom);
	}

	public void stopscan()
	{
		mSongScanner = new SongScanner(this);
		if(mSongScanner.IsScanning())
		{
			mSongScanner.StopScan();
		}
		Scanning = false;
	}

	public void IgnoreUser(string Username)
	{
		if (Username.Length > 2 )
		{
			int suceeded = 0;
			ci = new CultureInfo("en-US");
			List<User> users  = GetAllUsers();
			foreach (User user in users)
			{
				if (user.User_Name.StartsWith(Username, true, ci))
				{
					List<int> copy  = new List<int>(ignored);
					foreach (int ignored_id in copy)
					{
						if (ignored_id == user.User_ID)
						{
							suceeded=2 ;
							SendChatMessage("Removing Ignore for: " + user.NameFormat() + ".");
							ignored.Remove(user.User_ID);
						}
					}

					if ( suceeded != 2)
					{
						suceeded = 1;
						SendChatMessage("Ignoring User " + user.NameFormat() + ".");
						ignored.Add(user.User_ID);
					}
				}
			}
			if (suceeded == 0 )
			{
				SendChatMessage("User: " + Func.ChatColor("ff0000") + Username + " " + Func.ChatColor("ffffff") + "not found on the server. Ignore failed.");
			}
		}
		else
		{
			SendChatMessage(Func.ChatColor("ff0000") + "You must give me a username that is 3 characters or longer" + Func.ChatColor("ffffff"));
		}

	}
	public void SendChatPM(string Username, string Message)
	{

		if (Username.Length > 2 )
		{
			int suceeded = 0;
			ci = new CultureInfo("en-US");
			List<User> users  = GetAllUsers();
			foreach (User user in users)
			{
				if (user.User_Name.StartsWith(Username, true, ci))
				{
					List<int> copy  = new List<int>(user.ignored);
					foreach (int ignored_id in copy)
					{
						if (ignored_id == User_ID)
						{
							suceeded = 2;
							SendChatMessage("User: " + Func.ChatColor("ff0000") + Username + " " + Func.ChatColor("ffffff") + "Has Ignored you. PM Failed.");
						}
					}
					if (suceeded != 2)
					{
						suceeded = 1;
						user.SendChatMessage(Message);
						MainClass.AddLog("PM From " + User_Name + " To: " + user.User_Name + " Me: " + Message);
						SendChatMessage("Sent PM to: " +  user.NameFormat() + "." );
					}
				}
			}
			if (suceeded == 0 )
			{
				SendChatMessage("User: " + Func.ChatColor("ff0000") + Username + " " + Func.ChatColor("ffffff") + "not found on the server. PM failed.");
			}
		}
		else
		{
			SendChatMessage(Func.ChatColor("ff0000") + "You must give me a username that is 3 characters or longer" + Func.ChatColor("ffffff"));
		}
	}


	public void scan()
	{
		if (Scanning)
		{
			SendChatMessage("Scan already in progress");
		}
		else
		{
			if (CurrentRoom == null)
			{
				SendChatMessage("Scanning can not be done from the Lobby. Join a room and then run /scan");
			}
			else
			{
				MySql.Query("DELETE FROM userpacks where userid = '" + User_ID + "'");
				mainClass.SendChatAll("Scan Started for " + NameFormat()+" ", CurrentRoom);
				mSongScanner = new SongScanner(this);
				Scanning = true;
				Hashtable[] packs = MySql.Query("select packname, title, subtitle, artist, packidentifiertrans.packid, titletrans, subtitletrans, artisttrans from packsongstrans, packidentifiertrans, packs where packidentifiertrans.packsongid = packsongstrans.id and packidentifiertrans.packid = packs.id order by packname asc");
				mSongScanner.LoadSongs(packs);

				mSongScanner.SendNextSong();
			}
		}
	}

	public void showpacks()
	{
		string userpacks = "";
		Hashtable[] showpacks = MySql.Query("select packs.abr as 'packname'  from userpacks,packs where packs.id = userpacks.packid and userpacks.userid = "+User_ID);
		if (showpacks.Count() == 0)
		{
			SendChatMessage("You have no Packs.");
			return;
		}
		foreach(Hashtable showpackshash in showpacks)
		{
			userpacks += (string)showpackshash["packname"];
			userpacks += ", ";
		}
		userpacks=userpacks.Remove(userpacks.Length-2);
		mainClass.SendChatAll(NameFormat()+"'s packs: " + userpacks, CurrentRoom);
	}

	public void commonpacks()
	{
		if (CurrentRoom == null)
		{
			SendChatMessage("Checking common packs can not be done from the Lobby. Join a room and then run /commonpacks");
			return;
		}
		User[] users = GetUsersInRoom();
		string useridquerystring = "";
		string commonpacks = "";
		int packcount = 0;
		int allusers = 0;
		int usercount = CurrentRoom.Users.Count();
		string packsperuser = "Users: ";
		foreach (User user in users)
		{
			Hashtable haspacks=MySql.Query("SELECT count(*) as 'count' from userpacks where userid  =  " + user.User_ID)[0];
			int pcount = (int)haspacks["count"];
			if (pcount > 0)
			{
				++allusers;
			}
			else
			{
				mainClass.SendChatAll(user.NameFormat() + " " +  Func.ChatColor("ffffff") + "Has not ran /scan yet or has no packs.", CurrentRoom);
			}
			packsperuser += user.NameFormat() + "("+pcount+"), ";
			useridquerystring += useridquerystring + "userid = " + user.User_ID + " or ";
		}
		packsperuser=packsperuser.Remove(packsperuser.Length-2);
		if (allusers == usercount)
		{
			useridquerystring=useridquerystring.Remove(useridquerystring.Length-3);
			Hashtable[] common = MySql.Query("select * from (select packs.abr as 'packname', packid,count(*) as count from userpacks,packs where packs.id = userpacks.packid and (" + useridquerystring + ") group by packid) as tmptable where count >="+usercount);
			mainClass.SendChatAll(packsperuser, CurrentRoom);
			if (common.Count() == 0)
			{

				mainClass.SendChatAll(Func.ChatColor("aa0000") + "No packs in Common!" + Func.ChatColor("ffffff"), CurrentRoom);
			}
			else
			{
				foreach(Hashtable commonhash in common)
				{
					commonpacks += (string)commonhash["packname"];
					commonpacks += ", ";
					++packcount;
				}
				commonpacks=commonpacks.Remove(commonpacks.Length-2);
				mainClass.SendChatAll("CP(" + packcount + "): "+commonpacks, CurrentRoom);
			}
		}
	}

	public void Kick()
	{
		MainClass.AddLog("Client '" + this.User_Name + "' kicked.");
		if (this.CurrentRoom != null) this.CurrentRoom = null;
		this.Disconnect();
	}

	public void roomban()
	{
		MainClass.AddLog("Client '" + this.User_Name + "' kicked and banned from room: " + this.CurrentRoom.Name);
		this.CurrentRoom.banned.Add(this.User_ID);
	}

	public void Ban()
	{
		Data.BanUser(this, 0);
	}

	public void Ban(int originID)
	{
		Data.BanUser(this, originID);
	}

	public void KickBan()
	{
		this.Ban();
		this.Kick();
	}

	public void KickBan(int originID)
	{
		this.Ban(originID);
		this.Kick();
	}

	public void Disconnect()
	{
		MainClass.AddLog("Client '" + this.User_Name + "' disconnected.");
		if (this.CurrentRoom != null)
			this.CurrentRoom.Users.Remove(this);
		this.tcpClient.Close();
		this.Connected = false;

		if ( (this.User_Name != "") && (this.User_Name != null) )
		{
			Hashtable[] checkforfriends = MySql.Query("select * from friends where friend = " + User_ID.ToString() + "");
			User[] allusers = GetUsersInServer();
			if (checkforfriends.Count() != 0 )
			{
				for (int i = 0; i < checkforfriends.Count(); i++)
				{
					foreach (User user in allusers)
					{
						if (user.User_ID == (int)checkforfriends[i]["user"])
						{
							string time = DateTime.Now.ToString("HH:mm:ss");
							user.SendChatMessage(Func.ChatColor("ffff00") + "Your friend: " + Func.ChatColor("ffffff") + "'" +NameFormat() + "'" + Func.ChatColor("ffff00") + " has disconnected from the server at " + time + ".");
						}
					}
				}
			}
		}
	}

	public string NameFormat()
	{
		string current = User_Name;

		for (int i = 0; i < mainClass.Scripting.NameFormatHooks.Count; i++)
		{
			try
			{
				current = mainClass.Scripting.NameFormatHooks[i](this, current);
			}
			catch (Exception ex)
			{
				mainClass.Scripting.HandleError(ex);
			}
		}

		return current + Func.ChatColor("ffffff");
	}

	public void SendChatMessage(string Message)
	{
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCCM));
			ez.WriteNT(" " + (Message.StartsWith("|c0") ? "" : Func.ChatColor("ffffff")) + Message + " ");
			MainClass.AddLog("Chat: " + OpenSMO.User.Utf8Decode(Message));
			ez.SendPack();
		}
	}

	public void SendRoomChatMessage(string Message)
	{
		mainClass.SendChatAll(NameFormat() + " got " + Message , CurrentRoom);
	}


	public void SendRoomList()
	{
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
			ez.Write1((byte)1);
			ez.Write1((byte)1);
	
			if (ShadowBanned)
			{
				ez.Write1((byte)0);
			}
			else
			{
				byte visibleRoomCount = 0;
				foreach (Room r in mainClass.Rooms)
				{
					if (!r.Owner.ShadowBanned)
						visibleRoomCount++;
				}
				ez.Write1(visibleRoomCount);
	
				foreach (Room room in mainClass.Rooms)
				{
					if (!room.Owner.ShadowBanned)
					{
						ez.WriteNT(room.Name);
						ez.WriteNT(room.Description);
					}
				}
	
				foreach (Room room in mainClass.Rooms)
				{
					if (!room.Owner.ShadowBanned)
						ez.Write1((byte)room.Status);
				}
	
				foreach (Room room in mainClass.Rooms)
				{
					if (!room.Owner.ShadowBanned)
						ez.Write1((byte)(room.Password != "" ? 1 : 0));
				}
			}
	
			ez.SendPack();
		}
	}

	public void SendToRoom()
	{
		SyncNeeded = false;
		CanPlay = true;

		lock(ez)
		{
			if (CurrentRoom != null)
			{
				SendRoomList();
	
				List<User> userlist = GetAllUsers();
				foreach (User user in userlist)
					user.SendRoomPlayers();
	
				ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
				ez.Write1(1);
				ez.Write1(0);
				ez.WriteNT(CurrentRoom.Name);
				ez.WriteNT(CurrentRoom.Description);
				ez.Write1(1); // If this is 0, it won't change the players' screen
				ez.SendPack();
	
				mainClass.SendChatAll(NameFormat() + Func.ChatColor("ffffff") + " joined the room.", CurrentRoom);
				newjoin = true;
			}
			else
			{
				ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
				ez.Write1(1);
				ez.Write1(0);
				ez.WriteNT("");
				ez.WriteNT("");
				ez.Write1((byte)NSScreen.Lobby); // If this is 0, it won't change the players' screen
				ez.SendPack();
	
				//MainClass.AddLog("Not supported: Kicking from room. Fixme! User::SendToRoom", true);
			}
		}
	}

	public User[] GetUsersInRoom()
	{
		List<User> ret = new List<User>();
		List<User> users = GetAllUsers();
		foreach (User user in users)
		{
			if (user.CurrentRoom == this.CurrentRoom)
				ret.Add(user);
		}
		return ret.ToArray();
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

	public void UpdateRoomStatus()
	{
		User[] serverusers = GetUsersInLobby();
		foreach (User user in serverusers)
		{
			user.SendRoomList();
		}
	}

	public User[] GetUsersInServer()
	{
		List<User> ret = new List<User>();
		List<User> users = GetAllUsers();
		foreach (User user in users)
		{
			ret.Add(user);
		}
		return ret.ToArray();
	}

	public List<User> GetAllUsers()
	{
		lock(mainClass.Users)
		{
			List<User> users  = new List<User>(mainClass.Users);
			return users;
		}
	}

	public void SendRoomPlayers()
	{
		User[] users = GetUsersInRoom();

		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCUUL));
			ez.Write1(mainClass.ServerMaxPlayers); // Not used clientside
	
			if (ShadowBanned)
			{
				ez.Write1((byte)1);
				ez.Write1(1);
	
				ez.WriteNT(User_Name+ "(" + User_Level_Rank  + ")");
			}
			else
			{
				ez.Write1((byte)users.Length);
	
				foreach (User user in users)
				{
					if ( (byte)user.CurrentScreen == 0 )
					{
						ez.Write1((byte)NSScreen.red);
					}
	
					if ( (byte)user.CurrentScreen == 1 )
					{
						ez.Write1((byte)NSScreen.blue);
					}
					if  ( (byte)user.CurrentScreen == 2 )
					{
						ez.Write1(1); // status
					}
					if ( (byte)user.CurrentScreen == 3 )
					{
						ez.Write1((byte)NSScreen.Options);
					}
					if ( ((byte)user.CurrentScreen > 3) )
					{
//		Causes errors for clients that care that they are missing states that other clients send it
//	        ez.Write1((byte)user.CurrentScreen); // status
						ez.Write1(1); // status
	
					}

					// Do not show rank of users with > 10 characters in their name. It makes it too long
					int namelength = Utf8Decode(user.User_Name).Length;
					if ( namelength > 9 )
					{
						ez.WriteNT(user.User_Name);
					}
					else
					{
						// Do not display rank if over 1000
						if ( (user.User_Level_Rank < 100 ) && namelength < 10 )
						{
							ez.WriteNT(user.User_Name + "(" + user.User_Level_Rank  + ")");
						}
						else if ( (user.User_Level_Rank < 1000 ) && namelength < 7 )
						{
							ez.WriteNT(user.User_Name + "(" + user.User_Level_Rank  + ")");
						}
						else
						{
							ez.WriteNT(user.User_Name);
						}
					}
				}
			}

			ez.SendPack();
		}
	}

	public void SendSong(bool Start)
	{
		if (Start)
		{
			Playing = true;
			CurrentRoom.Status = RoomStatus.Closed;
			UpdateRoomStatus();
			// Reset
			Notes = new int[(int)NSNotes.NUM_NS_NOTES];
			Score = 0;
			Combo = 0;
			MaxCombo = 0;
			Grade = NSGrades.AAAA;
		}

		if ( CurrentRoom != null)
		{
			if (CurrentRoom.CurrentSong !=  null )
			{
				if ( ((CurrentRoom.CurrentSong.PICKName == "") && (CurrentRoom.CurrentSong.PICKSubTitle == "") && (CurrentRoom.CurrentSong.PICKArtist == "")) )
				{
					//Do Nothing
				}
				else
				{
					lock(ez)
					{
						ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCRSG));
						ez.Write1(Start ? (byte)2 : (byte)1);
						ez.WriteNT(CurrentRoom.CurrentSong.PICKName);
						ez.WriteNT(CurrentRoom.CurrentSong.PICKArtist);
						ez.WriteNT(CurrentRoom.CurrentSong.PICKSubTitle);
						ez.SendPack();
					}
				}
			}
		}
	}


	public void ScanSong(string title, string subtitle, string artist)
	{
		title = Utf8Encode(title);
		subtitle = Utf8Encode(subtitle);
		artist = Utf8Encode(artist);
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCRSG));
			ez.Write1((byte)1);
			ez.WriteNT(title);
			ez.WriteNT(artist);
			ez.WriteNT(subtitle);
			ez.SendPack();
		}
	}


	public void SendGameStatusColumn(byte ColumnID)
	{
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCGSU));
			ez.Write1(ColumnID);

			User[] origColumnUsers = GetUsersInRoom();
			User[] columnUsers = (from user in origColumnUsers where user.Playing orderby user.SMOScore descending select user).ToArray();
			ez.Write1((byte)columnUsers.Length);

			switch (ColumnID)
			{
			case 0: // Positions
				for (int i = 0; i < columnUsers.Length; i++)
				{
					for (int j = 0; j < origColumnUsers.Length; j++)
					{
						if (origColumnUsers[j] == columnUsers[i])
						{
							ez.Write1((byte)j);
							break;
						}
					}
				}
				break;

			case 1: // Combo
				foreach (User user in columnUsers)
					ez.Write2((short)user.Combo);
				break;

			case 2: // Grade
				foreach (User user in columnUsers)
					ez.Write1((byte)user.Grade);
				break;
			}

			ez.SendPack();
		}
	}

	public void SendGameStatus()
	{
		SendGameStatusColumn(0);
		SendGameStatusColumn(1);
		SendGameStatusColumn(2);
	}

	public bool IsModerator()
	{
		return User_Rank >= UserRank.Moderator;
	}

	public bool IsAdmin()
	{
		return User_Rank >= UserRank.Admin;
	}

	public bool CanChangeRoomSettings()
	{
		if (CurrentRoom != null)
			return CurrentRoomRights >= RoomRights.Operator || IsModerator();
		return false;
	}

	public void SendAttack(string modifiers, int ms)
	{
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCAttack));
			ez.Write1(0); // iPlayerNumber <-- Most of the times 0.
			ez.Write4(ms); // fSecsRemaining / 1000.0f <-- Thus, in milliseconds.
			ez.WriteNT(modifiers); // "300% wave, *4 -300% beat" <-- Deadly.
			ez.SendPack();
		}
	}

	public void SendSongStartTo(User[] checkSyncPlayers)
	{
		lock(checkSyncPlayers)
		{
			foreach (User user in checkSyncPlayers)
			{
				user.Synced = false;
				user.SongTime.Restart();
				lock(ez)
				{
					user.ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCGSR));
					user.ez.SendPack();
				}
			}
		}
	}


	public void NSCPing()
	{
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCPingR));
			ez.Discard(); // Just to be sure.
		}
	}
	

	public void NSCPingR()
	{
		pingTimeout = 1;
		ez.Discard(); // Just to be sure.
	}

	public void NSCHello()
	{
		User_Protocol = ez.Read1();
		User_Game = ez.ReadNT().Replace("\n", "|");

		MainClass.AddLog(User_IP + " is using SMOP v" + User_Protocol.ToString() + " in " + User_Game);
		PlayTime.Start();
		lock(ez)
		{
			ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCHello));
			ez.Write1(mainClass.ServerVersion);
			ez.WriteNT(mainClass.ServerConfig.Get("Server_Name"));
			ez.SendPack();
		}
	}

	public void NSCSMS()
	{
		NSScreen oldScreen = CurrentScreen;
		NSScreen newScreen = (NSScreen)ez.Read1();

		User[] lobbyusers = GetUsersInRoom();
		foreach(User lobbyuser in lobbyusers)
		{
			lobbyuser.SendRoomList();
			lobbyuser.SendRoomPlayers();
		}
		if (newScreen == NSScreen.Lobby)
		{
			CurrentRoom = null;

			CurrentRoomRights = RoomRights.Player;
			CurrentScreen = newScreen;
			User[] roomusers = GetUsersInRoom();
			foreach(User roomuser in roomusers)
			{
				roomuser.SendRoomList();
				roomuser.SendRoomPlayers();
			}
		}
		else
		{
		
			if (newScreen == NSScreen.Room)
			{
				SendSong(false);
			}
//	}else if (newScreen == NSScreen.Room) {
//        // find people waiting for synchronization
//        List<User> usersToSendPacketTo = new List<User>();
//        foreach (User user in CurrentRoom.Users) {
//          if (user.SyncNeeded) {
//            usersToSendPacketTo.Add(user);
//          }
//        }

//      } else if (newScreen == NSScreen.Room) {
//        List<User> users = CurrentRoom.Users;
//        // find people waiting for synchronization
//        List<User> usersToSendPacketTo = new List<User>();
//        foreach (User user in users) {
//          if (user.SyncNeeded) {
//            usersToSendPacketTo.Add(user);
//          }
//        }
//
//        // send packet those people
//        SendSongStartTo(usersToSendPacketTo.ToArray());

			CurrentScreen = newScreen;

			User[] usersinroom = GetUsersInRoom();
			foreach (User user in usersinroom)
			{
				user.SendRoomPlayers();
			}
		}
	}

	public void NSCGSR()
	{
		if (CurrentRoom == null)
		{
			ez.Discard();
			return;
		}

		if (!RequiresAuthentication()) return;

		GameFeet = ez.Read1() / 16;
		GameDifficulty = (NSDifficulty)(ez.Read1() / 16);

		Synced = ez.Read1() == 16;

		CurrentRoom.CurrentSong.Name = ez.ReadNT();
		CurrentRoom.CurrentSong.SubTitle = ez.ReadNT();
		CurrentRoom.CurrentSong.Artist = ez.ReadNT();

		this.CourseTitle = ez.ReadNT();
		this.SongOptions = ez.ReadNT();
		string[] options = this.SongOptions.Split(',');
		foreach (string option in options)
		{
			if (option.Contains("xMusic"))
			{
				string[] rate = option.Split('x');
				this.PlayerRate = (int)Math.Round( (float.Parse(rate[0]) * 100.0) );
				if (this.PlayerRate == 100)
				{
					this.PlayerRate = 0;
				}
			}
			else
			{
				this.PlayerRate = 100;
			}
		}
		string newPlayerSettings = "";
		do
		{
			newPlayerSettings += ez.ReadNT() + " ";
		}
		while (ez.LastPacketSize > 0);
		GamePlayerSettings = newPlayerSettings.Trim();

		if (User_Protocol == 2)
		{
			GamePlayerSettings = GamePlayerSettings.Substring(0, GamePlayerSettings.LastIndexOf(" ")<0?0:GamePlayerSettings.LastIndexOf(" "));
		}

		//Remove non unique Entries:

		string[] settings = GamePlayerSettings.Replace(",", "").Split(' ');
		string[] uniquesettings = settings.Distinct().ToArray();
		GamePlayerSettings = string.Join(", ", uniquesettings);
		CurrentRoom.AllPlaying = true;
		User[] checkSyncPlayers = GetUsersInRoom();

		if (!gotstart)
		{

			MainClass.AddLog("SENT NSCGSR");
			// Waiting for other players to sync.
			lock(checkSyncPlayers)
			{
				if (checkSyncPlayers != null)
				{
					SendSongStartTo(checkSyncPlayers);
				}
			}
			gotstart = true;
			waittime.Restart();
		}
		else
		{
			waittime.Stop();
			gotstart = false;
			waiting = true;
			MainClass.AddLog("SENT SYNC NSCGSR");
			skippacket = false;
			lock(checkSyncPlayers)
			{
				if (checkSyncPlayers != null)
				{
					mSyncStart = new SyncStart(this);
					Thread syncstartT = new Thread(() => mSyncStart.StartWhenAllSynced(checkSyncPlayers));
					if (!syncstartT.IsAlive)
					{
						syncstartT.Start();
					}
				}
			}
		}
	}

	public static int GetServCombo(int NoteHit, int servcombo)
	{
		switch (NoteHit)
		{
		case 10:
			break;
		case 9:
			break;
		case 8:
			servcombo++;
			break;
		case 7:
			servcombo++;
			break;
		case 6:
			servcombo++;
			break;

		default:
			servcombo=0;
			break;
		}
		return servcombo;
	}

	public static int GetPerfMarv(int NoteHit, int perfmarv, int jump)
	{
		switch (NoteHit)
		{
		case 10:
			break;
		case 9:
			break;
		case 8:
			perfmarv =  perfmarv + 1 + jump;
			break;
		case 7:
			perfmarv =  perfmarv + 1 + jump;
			break;
		default:
			perfmarv =  0;
			break;
		}
		return perfmarv;
	}

	public static int GetJumpCounts(int NoteHit, int jump, int jumpxp)
	{
		switch (NoteHit)
		{
		case 8:
			jumpxp += jump * 5;
			break;
		case 7:
			jumpxp += jump * 4;
			break;
		case 6:
			jumpxp += jump * 3;
			break;
		case 5:
			jumpxp += jump * 2;
			break;
		case 4:
			jumpxp += jump * 1;
			break;
		}
		return jumpxp;
	}




	public static int Judge(double NoteOffset)
	{
		double smarv  = -.022505;
		double sperf  = -.045005;
		double sgreat = -.090005;
		double sgood  = -.135005;
		double sboo   = -.189005;


		if ((NoteOffset > smarv) && (NoteOffset < (smarv * -1d)))
		{
			return 8;
		}
		else if ((NoteOffset > sperf) && (NoteOffset < (sperf * -1d)))
		{
			return 7;
		}
		else if ((NoteOffset > sgreat) && (NoteOffset < (sgreat * -1d)))
		{
			return 6;
		}
		else if ((NoteOffset > sgood) && (NoteOffset < (sgood * -1d)))
		{
			return 5;
		}
		else if ((NoteOffset > sboo) && (NoteOffset < (sboo * -1d)))
		{
			return 4;
		}
		else
		{
			return 3;
		}
	}


	public static int GetJudge(int NoteHit, double NoteOffset)
	{
		int JudgeNote = 0;

		switch (NoteHit)
		{
		case 8:
		case 7:
		case 6:
		case 5:
		case 4:
			JudgeNote = Judge(NoteOffset);
			return JudgeNote;
		default:
			return (int)NoteHit;
		}
	}

	public static string Human(int seconds)
	{
		int minutes = seconds/60;
		string human = "";
		if (minutes > 0)
		{
			seconds= seconds - (minutes * 60);
			if ( seconds < 10 )
			{
				human = minutes.ToString() + "m0" + seconds.ToString() + "s";
			}
			else
			{
				human = minutes.ToString() + "m" + seconds.ToString() + "s";
			}
		}
		else
		{
			human = seconds.ToString() + "s";
		}
		return human;
	}

	public static int GetTiming(int NoteHit, double NoteOffset, int timing)
	{
		//Default timing windows
		double smarv  = -.02255;
		double sperf  = -.04505;
		double sgreat = -.09005;
		double sgood  = -.13505;
		double sboo   = -.18905;
		switch (NoteHit)
		{
		case 8:
			if ((NoteOffset < smarv) || (NoteOffset > (smarv * -1d)))
			{
				timing++;
			}
			break;
		case 7:
			if ((NoteOffset < sperf) || (NoteOffset > (sperf * -1d)))

			{
				timing++;
			}
			break;
		case 6:
			if ((NoteOffset < sgreat) || (NoteOffset > (sgreat * -1d)))

			{
				timing++;
			}
			break;
		case 5:
			if ((NoteOffset < sgood) || (NoteOffset > (sgood * -1d)))

			{
				timing++;
			}
			break;
		case 4:
			if ((NoteOffset < sboo) || (NoteOffset > (sboo * -1d)))

			{
				timing++;
			}
			break;
		}
		return timing;
	}


	public void NSCGSU()
	{
		if (!RequiresAuthentication()) return;

		if ((Playing && !Spectating) && (this.CurrentRoom != null))
		{
			NSNotes gsuCtr;
			NSGrades gsuGrade;
			int gsuScore, gsuCombo, gsuLife;
			double gsuOffset;

			gsuCtr = (NSNotes)ez.Read1();
			gsuGrade = (NSGrades)(ez.Read1() / 16);
			gsuScore = ez.Read4();
			gsuCombo = ez.Read2();
			gsuLife = ez.Read2();
			NoteOffsetRaw = ez.ReadU2();
			gsuOffset = NoteOffsetRaw / 2000d - 16.384d;

			if (User_Protocol == 2)
				gsuCtr += 2;

			NoteHit = gsuCtr;
			NoteOffset = gsuOffset;
			NoteHit = (NSNotes)GetJudge((int)NoteHit, NoteOffset);

//        MainClass.AddLog("NoteHit: " + NoteHit);
//        MainClass.AddLog("NoteOffset: " + NoteOffset);
//	timing = GetTiming((int)NoteHit, NoteOffset, timing);
//	MainClass.AddLog(this.User_Name"'s Timing: " + timing);
			try
			{
				Notes[(int)NoteHit]++;
			}
			catch (Exception e)
			{
				MainClass.AddLog("gsuCtr:" + gsuCtr);
				foreach(var note in Notes)
				{
					MainClass.AddLog(note.ToString());
				}
				SendChatMessage("Your client gave the server weird info and was kicked...");
				SendRoomChatMessage(this.User_Name + " has been kicked for sending bad data to the server");
				MainClass.AddLog(this.User_Name + " has been kicked for sending bad array to server");
				Kick();
				Console.WriteLine("{0} Exception caught.", e);

			}

			Grade = gsuGrade;
			Score = gsuScore;
			Combo = gsuCombo;

			servcombo = GetServCombo((int)NoteHit, servcombo);
			jump = Combo - servcombo;

			if (( jump > 3 ) || ( jump < 0 ))
			{
				jump=0;
			}

			servcombo = Combo;

//	timing = GetTiming((int)NoteHit, NoteOffset, timing);

			perfmarv = GetPerfMarv((int)NoteHit, perfmarv, jump);
			if ( perfmarv > 249 )
			{
				toasty++;
//		MainClass.AddLog("TOOOOOOOAAAAAAASSSSSSTYYYYYYYYYYYYY");
				perfmarv = 0;
			}

			if ( jump > 0 )
			{
				jumpxp = GetJumpCounts((int)NoteHit, jump, jumpxp);
			}

			if ((NoteOffset < 0.04509) && (NoteOffset > -.04509))
			{
				clientoffsetcount++;
				clientoffset += NoteOffset;
				if (NoteOffset < 0.0)
				{
					offsetneg++;
				}
				if (NoteOffset > 0.0)
				{
					offsetpos++;
				}
			}


			if (gsuCombo > MaxCombo)
				MaxCombo = gsuCombo;
		}
		else
			ez.Discard();
	}

	public void NSCGON()
	{
		if (!RequiresAuthentication()) return;

		if (Playing && !Spectating)
		{
			lock(ez)
			{
				if (CurrentRoom != null)
				{
					// Required for SMOP v2
					CurrentRoom.Reported = false;

					User[] origColumnUsers = GetUsersInRoom();
					User[] columnUsers = (from user in origColumnUsers where user.Playing orderby user.SMOScore descending select user).ToArray();

					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCGON));
					ez.Write1((byte)columnUsers.Length);

					// Name index
					for (int i = 0; i < columnUsers.Length; i++)
					{
						for (int j = 0; j < origColumnUsers.Length; j++)
						{
							if (origColumnUsers[j] == columnUsers[i])
								ez.Write1((byte)j);
						}
					}
					// Name index
					//              for (int i = 0; i < columnUsers.Length; i++) { for (int j = 0; j < origColumnUsers.Length; j++) { if (origColumnUsers[j] == columnUsers[i]) { ez.Write1((byte)id); }
					// Score
					for (int i = 0; i < columnUsers.Length; i++) ez.Write4(columnUsers[i].Score);
					// Grade
					for (int i = 0; i < columnUsers.Length; i++) ez.Write1((byte)columnUsers[i].Grade);
					// Difficulty
					for (int i = 0; i < columnUsers.Length; i++) ez.Write1((byte)columnUsers[i].GameDifficulty);

					// Flawless to Miss
					for (int j = 0; j < 6; j++)
						for (int i = 0; i < columnUsers.Length; i++) ez.Write2((short)columnUsers[i].Notes[(int)NSNotes.Flawless - j]);
					// Held
					for (int i = 0; i < columnUsers.Length; i++) ez.Write2((short)columnUsers[i].Notes[(int)NSNotes.Held]);
					// Max combo
					for (int i = 0; i < columnUsers.Length; i++) ez.Write2((short)columnUsers[i].MaxCombo);

					// Player settings + percent
					for (int i = 0; i < columnUsers.Length; i++)
					{
						int marv = columnUsers[i].Notes[(int)NSNotes.Flawless];
						int perf = columnUsers[i].Notes[(int)NSNotes.Perfect];
						int grea = columnUsers[i].Notes[(int)NSNotes.Great];
						int good = columnUsers[i].Notes[(int)NSNotes.Good];
						int boo  = columnUsers[i].Notes[(int)NSNotes.Barely];
						int miss = columnUsers[i].Notes[(int)NSNotes.Miss];
						int ok   = columnUsers[i].Notes[(int)NSNotes.Held];
						int ng   = columnUsers[i].Notes[(int)NSNotes.NG];
						Tpnt = (3 * marv) + (2 * perf) + grea - (4 * boo) - (8 * miss) + (6 * ok);
						Tmaxpnt = 3 * (marv + perf + grea + good + boo + miss) + 6 * (ok + ng);
						percentf = (Tpnt/Tmaxpnt)*100F;
						percent = percentf.ToString("n2");
						columnUsers[i].percent=percent;
						string settings = columnUsers[i].GamePlayerSettings;
						string percset = percent + "%, " + settings;
						if (timing > 2)
						{
							percset = percset + ", TIMING";
						}
						ez.WriteNT(percset);
					}

					ez.SendPack();
				}
			}


//	Data.PrintInfo(smoUsername, percent);
			if (this.CurrentRoom != null)
			{
				if ( CurrentRoom.Password != "" )
				{
					CurrentRoom.Status = RoomStatus.Locked;
				}
				else
				{
					CurrentRoom.Status = RoomStatus.Ready;
				}
				UpdateRoomStatus();
			}


			if (NoteCount > 0)
			{
//          if (FullCombo) SendChatMessage(Func.ChatColor("00aa00") + "FULL COMBO!!");
				Data.AddStats(this);
			}
		}
		else
		{
			if (Spectating)
				SendChatMessage(Func.ChatColor("aa0000") + "Spectator mode activated, no stats gained.");
		}
	}

	public static string Utf8Decode(string utf8me)
	{
		return Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes(utf8me));
	}

	public static string Utf8Encode(string text)
	{
		//return Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes(text));
		return Encoding.GetEncoding(28591).GetString(Encoding.GetEncoding(65001).GetBytes(text));
	}



	public void NSCRSG()
	{
		if (CurrentRoom == null)
		{
			ez.Discard();
			return;
		}

		if (!RequiresAuthentication()) return;

		byte pickResponseStatus = ez.Read1();

		string pickName = ez.ReadNT();
		string pickArtist = ez.ReadNT();
		string pickAlbum = ez.ReadNT();

		int timewaited;
		if ( (SelectTime.ElapsedMilliseconds < 751) && (SelectTime.ElapsedMilliseconds != 0) )
		{
			timewaited = 0;
		}
		else
		{
			timewaited = 1;
		}
		SelectTime.Restart();

		if (mSongScanner != null && mSongScanner.IsScanning())
		{
			string packname = mSongScanner.GetCurrentPackName();
			int packid = mSongScanner.GetCurrentPackID();
			switch (pickResponseStatus)
			{
			case 0: // Player has song
				CanPlay = SyncNeeded = true;
				//Has pack or not debug output in room
				//mainClass.SendChatAll(NameFormat() + " has pack: " + packname + " ID: " + packid, CurrentRoom);
				MySql.Query("INSERT INTO userpacks (userid,packid) VALUES('" + User_ID + "','" + packid + "')");
				ScanResponse = true;
				ez.Discard();
				break;

			case 1: // Player does not have song
				CanPlay = SyncNeeded = false;
				ScanResponse = true;
				ez.Discard();
				break;
			}

			mSongScanner.SendNextSong();
			return;
		}


		switch (pickResponseStatus)
		{
		case 0: // Player has song
			CanPlay = SyncNeeded = true;
			ez.Discard();
			return;

		case 1: // Player does not have song
			CanPlay = SyncNeeded = false;
			mainClass.SendChatAll(NameFormat() + " does " + Func.ChatColor("aa0000") + "not" + Func.ChatColor("ffffff") + " have that song!", CurrentRoom);
			ez.Discard();
			return;
		}


		User[] pickUsers = GetUsersInRoom();

		bool canStart = true;
		string cantStartReason = "";

		Song currentSong = CurrentRoom.CurrentSong;
		bool isNewSong = currentSong.Artist != pickArtist ||
						 currentSong.Name != pickName ||
						 currentSong.SubTitle != pickAlbum;

		foreach (User user in pickUsers)
		{
			if (user.CurrentScreen != NSScreen.Room)
			{
				canStart = false;
				cantStartReason = user.NameFormat() + " is not ready yet!";
				break;
			}
			else if (user.Scanning)
			{
				canStart = false;
				cantStartReason = user.NameFormat() + " is Pack Scanning!";
				break;
			}
			else if (timewaited == 0)
			{
				canStart = false;
				cantStartReason = "Please wait .75 seconds!!!";
				break;
			}
			else if (!user.CanPlay && !isNewSong && !newjoin )
			{
				canStart = false;
				cantStartReason = user.NameFormat() + " Lacks so not starting!";
				break;
			}
		}

		if (CurrentRoom.Free || CanChangeRoomSettings())
		{
			if (CurrentRoom.CurrentSong.Name == pickName &&
					CurrentRoom.CurrentSong.Artist == pickArtist &&
					CurrentRoom.CurrentSong.SubTitle == pickAlbum)
			{

				if (canStart)
				{
					mainClass.SendChatAll(NameFormat() + " has started the song.", CurrentRoom);
					foreach (User user in pickUsers)
					{
						Data.AddSong(true, this);
						user.SendSong(true);
						user.SendGameStatus();
					}
				}
				else
					mainClass.SendChatAll(cantStartReason, CurrentRoom);
			}
			else
			{
				if (canStart)
				{
					Song newSong = new Song();

					newSong.Name = pickName;
					newSong.Artist = pickArtist;
					newSong.SubTitle = pickAlbum;

					CurrentRoom.CurrentSong = newSong;

					CurrentRoom.CurrentSong.PICKName = pickName;
					CurrentRoom.CurrentSong.PICKArtist = pickArtist;
					CurrentRoom.CurrentSong.PICKSubTitle = pickAlbum;

					string htime = "";
					int pickSongPlayed = 0;
					int newSongID = 0;
					Hashtable pickSongRow = Data.AddSong(false, this);
					if (pickSongRow != null)
					{
						newSongID = (int)pickSongRow["ID"];
						pickSongPlayed = (int)pickSongRow["Played"];
						newSong.Time = (int)pickSongRow["Time"];
						htime = Human(newSong.Time);
					}

					if (pickSongPlayed != 0 )
					{
						Hashtable pickSongPlayedRow = Data.SongPlayed(newSongID);
						if ( (int)pickSongPlayedRow["Played"] < 1 )
						{
							pickSongPlayed = (int)pickSongPlayedRow["Played"];
						}
					}
//	    int asciilength = pickName.Length;

//	    int utf8length = Utf8Decode(pickName).Length;
//	    int textdiff = asciilength - utf8length;
//	    string chatname =  Utf8Decode(pickName);
//	    for (int i = 0; i < textdiff; i++)
//	    {
//			chatname = "\n" + chatname + "\n";
//	    }
					if (newSong.Time != 0)
					{
						mainClass.SendChatAll(NameFormat() + " selected " + Func.ChatColor("00aa00") + pickName + Func.ChatColor("ffffff") + Func.ChatColor("ffffff") + ", which has " + (pickSongPlayed == 0 ? "never been played." : (pickSongPlayed > 1 ? "been played " + pickSongPlayed.ToString() + " times " : "never been played.") + " and is " + htime + " long." ), CurrentRoom);
					}
					else
					{
						mainClass.SendChatAll(NameFormat() + " selected " + Func.ChatColor("00aa00") + pickName + Func.ChatColor("ffffff") + Func.ChatColor("ffffff") + ", which has " + (pickSongPlayed == 0 ? "never been played." : (pickSongPlayed > 1 ? "been played " + pickSongPlayed.ToString() + " times." : "never been played.") ), CurrentRoom);
					}
					newjoin = false;
					foreach (User user in pickUsers)
					{
						user.SendSong(false);
						user.SongTime.Reset();
					}
				}
				else
					mainClass.SendChatAll(cantStartReason, CurrentRoom);
			}
		}
		else
		{
			ez.Discard();
			SendChatMessage("You are not the room owner. Ask " + CurrentRoom.Owner.NameFormat() + " for /free");
		}
	}

	public void NSCUPOpts()
	{
		ez.Discard(); // This contains a string with user options, but we don't really care about that too much for now.
	}

	byte packetCommandSub = 0;

	public void NSCSMOnline()
	{
		packetCommandSub = ez.Read1();

		//Client requested more info on room
		if (packetCommandSub == 3)
		{
			string joinRoomName = ez.ReadNT();
			ez.Discard();
			foreach (Room room in mainClass.Rooms)
			{
				if (room.Name == joinRoomName)
				{
					//MainClass.AddLog("Found room name : " + room.Name);
					MainClass.AddLog(Utf8Decode(room.CurrentSong.Name) + ", " + Utf8Decode(room.CurrentSong.SubTitle) + ", " + Utf8Decode(room.CurrentSong.Artist));
					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
					ez.Write1((byte)3);
					ez.WriteNT(room.CurrentSong.Name);
					ez.WriteNT(room.CurrentSong.SubTitle);
					ez.WriteNT(room.CurrentSong.Artist);
					ez.Write1((byte)room.Users.Count());
					ez.Write1((byte)32);
					foreach (User user in room.Users)
					{
						ez.WriteNT(user.User_Name);
					}
					return;

				}
			}

		}

		byte packetCommandSubSub = ez.Read1();

		if (packetCommandSub == 0)   // Login
		{
			ez.Read1(); // Reserved byte

			string smoUsername = ez.ReadNT();
			string smoPassword = ez.ReadNT();

			if (!new Regex("^([A-F0-9]{32})$").Match(smoPassword).Success)
			{
				lock(ez)
				{
					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
					ez.Write2(1);
					ez.WriteNT("Login failed! Invalid password.");
					ez.SendPack();
				}

				MainClass.AddLog("Invalid password hash given!", true);
				return;
			}

			if ( smoUsername.Length > 32 )
			{
				lock(ez)
				{
					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
					ez.Write2(1);
					ez.WriteNT("Login failed! Name too Long");
					ez.SendPack();
				}

				MainClass.AddLog("Login name too long:" + smoUsername, true);
				return;
			}

			Hashtable[] smoLoginCheck = MySql.Query("SELECT * from users where Username='" + MySql.AddSlashes(smoUsername) + "'");
			if (smoLoginCheck.Length == 1 && smoLoginCheck[0]["Password"].ToString() == smoPassword)
			{
				MainClass.AddLog(smoUsername + " logged in.");

				User_Table = smoLoginCheck[0];
				User_ID = (int)User_Table["ID"];
				User_Name = (string)User_Table["Username"];
				User_Rank = (UserRank)User_Table["Rank"];
				int User_XP = (int)User_Table["XP"];

				Hashtable[] checkstasrank = MySql.Query("select count(*) as 'levelrank' from users where xp > '" + User_XP.ToString() + "'");
				Rank_Table =  checkstasrank[0];
				User_Level_Rank = (int)Rank_Table["levelrank"] + 1;

				MySql.Query("INSERT INTO connectionlog (userid,username,password,ip,result,clientversion) VALUES('" + User_ID + "','" + MySql.AddSlashes(smoUsername) + "','" + smoPassword + "','" + User_IP + "','suceeded','" + MySql.AddSlashes(User_Game) + "')");

				User[] checkifconnected = GetUsersInServer();
				foreach (User user in  checkifconnected)
				{
					if (user.User_Name.ToString() == this.User_Name.ToString())
					{
						connectioncount++;
					}
					if (connectioncount > 1 )
					{
						MainClass.AddLog("Kicking user " + this.User_Name.ToString() + " for duplicate login attempt");
					}
				}
				if (connectioncount > 1)
				{
					User[] kickconnected = GetUsersInServer();
					foreach (User user in  kickconnected)
					{
						if (user.User_Name.ToString() == this.User_Name.ToString())
						{
							user.Kick();
							break;
						}
					}
				}


				connectioncount = 0;
				lock(ez)
				{
					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
					ez.Write2(0);
					ez.WriteNT("Login success!");
					ez.SendPack();
				}

				SendChatMessage(mainClass.ServerConfig.Get("Server_MOTD"));
				string builddate = mainClass.builddate;
				SendChatMessage("Server was last updated on: "+ builddate);

				SendChatMessage("If you have not yet already Please create a room and run /scan");
				Hashtable[] checkforfriends = MySql.Query("select * from friends where friend = " + User_ID.ToString() + "");
				User[] allusers = GetUsersInServer();
				if (checkforfriends.Count() != 0 )
				{
					for (int i = 0; i < checkforfriends.Count(); i++)
					{
						foreach (User user in allusers)
						{
							if (user.User_ID == (int)checkforfriends[i]["user"])
							{
								string time = DateTime.Now.ToString("HH:mm:ss");
								user.SendChatMessage(Func.ChatColor("ffff00") + "Your friend: " + Func.ChatColor("ffffff") + "'" +NameFormat() + "'" + Func.ChatColor("ffff00") + " has connected to the server at " + time + "." + Func.ChatColor("ffffff"));
							}
						}
					}
				}

				Hashtable[] checkforusers = MySql.Query("select * from friends where user = " + User_ID.ToString() + "");
				User[] allusersfriends = GetUsersInServer();
				if (checkforusers.Count() != 0 )
				{
					for (int i = 0; i < checkforusers.Count(); i++)
					{
						foreach (User user in allusersfriends)
						{
							if (user.User_ID == (int)checkforusers[i]["friend"])
							{
								SendChatMessage(Func.ChatColor("ffff00") + "Your friend: " + Func.ChatColor("ffffff") + "'" + user.NameFormat() + "'" + Func.ChatColor("ffff00") + " Is on the server." + Func.ChatColor("ffffff"));
							}
						}
					}
				}



				SendRoomList();

				User[] users = GetUsersInRoom();
				foreach (User user in users)
					user.SendRoomPlayers();

				return;
			}
			else if (smoLoginCheck.Length == 0)
			{
				if (bool.Parse(mainClass.ServerConfig.Get("Allow_Registration")))
				{
					MySql.Query("INSERT INTO users (Username,Password,Email,Rank,XP) VALUES(\'" + MySql.AddSlashes(smoUsername) + "\',\'" + MySql.AddSlashes(smoPassword) + "\',\'\',0,0)");
					MainClass.AddLog(smoUsername + " is now registered with hash " + smoPassword);

					User_Table = MySql.Query("SELECT * FROM users WHERE Username='" + MySql.AddSlashes(smoUsername) + "' AND Password='" + MySql.AddSlashes(smoPassword) + "'")[0];
					User_ID = (int)User_Table["ID"];
					User_Name = (string)User_Table["Username"];
					User_Rank = (UserRank)User_Table["Rank"];

					MySql.Query("INSERT INTO connectionlog (userid,username,password,ip,result,clientversion) VALUES('" + User_ID + "','" + MySql.AddSlashes(User_Name) + "','" + smoPassword + "','" + User_IP + "','suceeded','" + MySql.AddSlashes(User_Game) + "')");

					lock (ez)
					{
						ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
						ez.Write2(0);
						ez.WriteNT("Login success!");
						ez.SendPack();
					}

					SendChatMessage(mainClass.ServerConfig.Get("Server_MOTD"));
					SendRoomList();

					User[] users = GetUsersInRoom();
					foreach (User user in users)
						user.SendRoomPlayers();

					return;
				}
			}


			MainClass.AddLog(smoUsername + " tried logging in with hash " + smoPassword + " but failed");
			MySql.Query("INSERT INTO connectionlog (userid,username,password,ip,result,clientversion) VALUES('" + User_ID + "','" + MySql.AddSlashes(smoUsername) + "','" + smoPassword + "','" + User_IP + "','failed','" + MySql.AddSlashes(User_Game) + "')");

			lock(ez)
			{
				ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCSMOnline));
				ez.Write2(1);
				ez.WriteNT("Login failed! Invalid password.");
				ez.SendPack();
			}
		}
		else if (packetCommandSub == 01)     // Join room
		{
			if (!RequiresAuthentication()) return;

			if (ez.LastPacketSize == 0)
				return;

			string joinRoomName = ez.ReadNT();
			string joinRoomPass = "";

			if (ez.LastPacketSize > 0)
				joinRoomPass = ez.ReadNT();

			int roombanned = 0;
			foreach (Room room in mainClass.Rooms)
			{
				if (room.Name == joinRoomName)
				{
					List<int> banned  = new List<int>(room.banned);
					foreach (int banned_id in banned)
					{
						if (banned_id == User_ID)
						{
							roombanned = 1;
							SendChatMessage("You have been banned from this room.");
						}
					}
				}

				if (room.Name == joinRoomName && (room.Password == joinRoomPass || IsModerator() || room.Password == joinpass) && roombanned == 0 )
				{
					CurrentRoom = room;
					SendToRoom();
					break;
				}
			}
		}
		else if (packetCommandSub == 02)     // New room
		{
			if (!RequiresAuthentication()) return;

			string newRoomName = ez.ReadNT();
			string newRoomDesc = ez.ReadNT();
			string newRoomPass = "";

			if (ez.LastPacketSize > 0)
				newRoomPass = ez.ReadNT();

			MainClass.AddLog(User_Name + " made a new room '" + newRoomName + "'");
			Room newRoom = new Room(mainClass, this);

			newRoom.Name = newRoomName;
			newRoom.Description = newRoomDesc;
			newRoom.Password = newRoomPass;

			mainClass.Rooms.Add(newRoom);

			User[] users = GetUsersInRoom();
			foreach (User user in users)
				user.SendRoomList();

			CurrentRoom = newRoom;

			if (this.CurrentRoom != null)
			{
				if ( CurrentRoom.Password != "" )
				{
					CurrentRoom.Status = RoomStatus.Locked;
				}
				else
				{
					CurrentRoom.Status = RoomStatus.Ready;
				}
			}
			CurrentRoomRights = RoomRights.Owner;
			SendToRoom();
			UpdateRoomStatus();
			SendChatMessage("Welcome to your room! Type /help for a list of commands.");
		}
		else
		{
			// This is probably only for command sub 3, which is information you get when you hover over a room in the lobby.
			// TODO: Make NSCSMOnline sub packet 3 (room info on hover)
			//MainClass.AddLog( "Discarded unknown sub-packet " + packetCommandSub.ToString() + " for NSCSMOnline" );
			ez.Discard();
		}
	}

	public void NSCSU()
	{
		ez.Discard();
	}

	public void NSCCM()
	{
		if (!RequiresAuthentication()) return;

		string cmMessage = ez.ReadNT();
		try
		{
			if (cmMessage[0] == '/')
			{
				string[] cmdParse = cmMessage.Split(new char[] { ' ' }, 2);
				string cmdName = cmdParse[0].Substring(1);
				bool handled = false;

				if (mainClass.Scripting.ChatCommandHooks.ContainsKey(cmdName))
				{
					for (int i = 0; i < mainClass.Scripting.ChatCommandHooks[cmdName].Count; i++)
					{
						bool subHandled = mainClass.Scripting.ChatCommandHooks[cmdName][i](this, cmdParse.Length == 2 ? cmdParse[1] : "");
						if (!handled) handled = subHandled;
					}
				}

				if (!handled)
					SendChatMessage("Unknown command. Type /help for a list of commands.");
			}
			else
			{
				bool cmHandled = false;
				for (int i = 0; i < mainClass.Scripting.ChatHooks.Count; i++)
					cmHandled = mainClass.Scripting.ChatHooks[i](this, cmMessage);
				if (!cmHandled)
					if ( nihongo == 1)
					{
						string sentcommand = "k";
						for (int i = 0; i < mainClass.Scripting.ChatCommandHooks[sentcommand].Count; i++)
						{
							mainClass.Scripting.ChatCommandHooks[sentcommand][i](this, cmMessage);
						}
					}
					else
					{
						mainClass.SendChatAll(NameFormat() + ": " + cmMessage, CurrentRoom);
					}
			}
		}
		catch (Exception ex)
		{
			mainClass.Scripting.HandleError(ex);
		}
	}

	void couldntReadData()
	{
		MainClass.AddLog("Couldn't read data from " + this.User_Name + ", user disconnecting");
		if (CurrentRoom != null) CurrentRoom = null;
		lock(mainClass.Users)
		{
			mainClass.Users.Remove(this);
		}
		this.tcpClient.Close();
	}

	int pingTimer = 0;
	int pingTimeout = 1;
	public void Update()
	{
		if (++pingTimer == mainClass.FPS)
		{
			if (pingTimeout > 0)
			{
				pingTimer = 0;
				pingTimeout = 1;
				lock(ez)
				{
					ez.Write1((byte)(mainClass.ServerOffset + NSCommand.NSCPing));
					ez.SendPack();
				}
			}
			else
			{
				if (pingTimeout == 0)
				{
					MainClass.AddLog("Ping timeout for " + this.User_Name + ", user disconnecting");
					if (CurrentRoom != null) CurrentRoom = null;
					lock(mainClass.Users)
					{
						mainClass.Users.Remove(this);
					}
					this.tcpClient.Close();
					return;
				}
				else
				{
					MainClass.AddLog("Timeout " + pingTimeout + " for user " + this.User_Name);
					pingTimeout--;
				}
			}
		}

		try
		{
			int a = tcpClient.Available;
		}
		catch
		{
			MainClass.AddLog("Socket closed.");
			if (CurrentRoom != null) CurrentRoom = null;
			lock(mainClass.Users)
			{
				mainClass.Users.Remove(this);
			}


			List<User> userlist = GetAllUsers();
			foreach (User user in userlist)
			{
				if (user.CurrentRoom == null)
				{
					user.SendRoomPlayers();
				}
			}
			return;
		}

		if (tcpClient.Available > 0)
		{
			try
			{
				if (ez.ReadPack() == -1) return;
			}
			catch
			{
				this.couldntReadData();
				return;
			}

			NSCommand packetCommand;
			try
			{
				packetCommand = (NSCommand)ez.Read1();
			}
			catch
			{
				this.couldntReadData();
				return;
			}

			switch (packetCommand)
			{
			case NSCommand.NSCPing:
				this.NSCPing();
				break;
			case NSCommand.NSCPingR:
				this.NSCPingR();
				break;
			case NSCommand.NSCHello:
				this.NSCHello();
				break;
			case NSCommand.NSCSMS:
				this.NSCSMS();
				break;
			case NSCommand.NSCGSR:
				this.NSCGSR();
				break;
			case NSCommand.NSCGSU:
				this.NSCGSU();
				break;
			case NSCommand.NSCGON:
				this.NSCGON();
				break;
			case NSCommand.NSCRSG:
				this.NSCRSG();
				break;
			case NSCommand.NSCUPOpts:
				this.NSCUPOpts();
				break;
			case NSCommand.NSCSMOnline:
				this.NSCSMOnline();
				break;
			case NSCommand.NSCSU:
				this.NSCSU();
				break;
			case NSCommand.NSCCM:
				this.NSCCM();
				break;
			default:
				MainClass.AddLog("Packet " + packetCommand.ToString() + " discarded!");
				ez.Discard();
				break;
			}

			if (mainClass.Scripting.PacketHooks.ContainsKey(packetCommand))
			{
				for (int i = 0; i < mainClass.Scripting.PacketHooks[packetCommand].Count; i++)
				{
					try
					{
						mainClass.Scripting.PacketHooks[packetCommand][i](new HookInfo()
						{
							User = this, SubCommand = packetCommandSub
						});
					}
					catch (Exception ex)
					{
						mainClass.Scripting.HandleError(ex);
					}
				}
			}
		}
	}
}
}
