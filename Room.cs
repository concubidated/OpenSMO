using System;
using System.Collections.Generic;
using System.Collections;

namespace OpenSMO
{
public enum RoomStatus : int { Ready=0, Locked, Closed }

public class Room
{
	public MainClass mainClass;

	public string ID = "";
	public string Name;
	public string Description;
	public string Password;
	public string Style = "dance";
	public List<int> banned = new List<int>();
	public RoomStatus Status = RoomStatus.Closed;
	public bool NoWebChat = false;
	public bool Free;
	public bool AllPlaying = false;
	public bool Secret = false;
	public int SendStatsTimer = 0;
	public Hashtable Meta = new Hashtable();
	public string myusers = "";
	public string lobbyusers = "";
	public int roomid = 0;

	public User Owner;
	public List<User> Users
	{
		get
		{
			List<User> ret = new List<User>();
			lock(mainClass.Users)
			{
				List<User> users  = new List<User>(mainClass.Users);
				foreach (User user in users)
				{
					if (user != null)
					{
						if (user.CurrentRoom == this)
							ret.Add(user);
					}
				}
				return ret;
			}
		}
	}
	public int UserCount
	{
		get
		{
			return Users.Count;
		}
	}
	public string UserList;

	public Song CurrentSong = new Song();
	public bool Reported;

	private string _ChatBuffer = "";
	public string ChatBuffer
	{
		get
		{
			string[] lines = _ChatBuffer.Split('\n');
			string ret = "";
			int lineLimit = int.Parse(mainClass.ServerConfig.Get("RTS_ChatLines"));
			int lineCount = lines.Length;
			int startCount = Math.Max(0, lineCount - lineLimit);

			for (int i = startCount; i < lineCount; i++)
				ret += lines[i] + '\n';
			return ret.Trim('\n');
		}
	}


	public string GetRoomUsers()
	{
		myusers ="";
		List<User> users  = new List<User>(mainClass.Users);
		foreach (User user in users)
		{
			if (user.CurrentRoom == this)
			{
				string quoted = user.User_ID + ":" + user.User_Name + ",";
				myusers += quoted;
			}
		}
		return myusers;
	}


	public void AddChatBuffer(string str)
	{
		_ChatBuffer += str + "\n";
	}

	public Room(MainClass mainClass, User Owner)
	{
		this.mainClass = mainClass;
		this.Owner = Owner;
		this.ID = MainClass.RandomString(5);
	}

	public void Update()
	{
		if (++SendStatsTimer == mainClass.FPS / 10)
		{
			foreach (User user in Users)
			{
				if (user.Playing)
					user.SendGameStatus();
				SendStatsTimer = 0;
			}
		}

		if (UserCount == 0)
		{
			MainClass.AddLog("Room '" + Name + "' removed.");
			mainClass.Rooms.Remove(this);
		}
	}
}
}
