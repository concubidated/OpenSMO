using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenSMO
{
public static class Data
{
	public static Hashtable GetSong(string Name, string Artist, string SubTitle)
	{
		Hashtable[] resCheck = MySql.Query("SELECT * FROM songs WHERE BINARY Name='" + Name + "' " +
										   "AND BINARY Artist='" + Artist + "' " +
										   "AND BINARY SubTitle='" + SubTitle + "' LIMIT 1");

		MainClass.AddLog("A DB select was made on (Name,Artist,Subtitle) VALUES('" + Name + "','" + Artist + "','" + SubTitle + "')");
		if (resCheck == null) return null;
		if (resCheck.Length == 1)
			return resCheck[0];
		else
			return null;
	}

	public static Hashtable SongPlayed(int ID)
	{
		Hashtable[] resCheck = MySql.Query("SELECT  count(*) AS 'Played' from stats where song ='" + ID + "'");
		return resCheck[0];
	}

	public static int CreateRoomDB(User user)
	{
		if (user.CurrentRoom == null || user.CurrentRoom.CurrentSong == null)
		{
			return -1;
		}
		else
		{
			string owner = MySql.AddSlashes(user.CurrentRoom.Owner.User_Name);
			string name = MySql.AddSlashes(user.CurrentRoom.Name);
			string desc = MySql.AddSlashes(user.CurrentRoom.Description);
			MySql.Query("INSERT INTO rooms (Name,Description,Owner) VALUES('" + name + "','" + desc + "','" + owner + "')");
			MainClass.AddLog("Owner: " + owner + " Name: " + name + "Description: " + desc);
			Hashtable[] getroomid = MySql.Query("SELECT ID from rooms where Name = '" + name + "' and Description = '" + desc + "' and Owner = '" + owner + "' ORDER BY created DESC LIMIT 1");
			Hashtable roomidhash = getroomid[0];
			int roomid = (int)roomidhash["ID"];
			return roomid;
		}
	}

	public static Hashtable AddSong(bool Start, User user)
	{
		if (user.CurrentRoom == null || user.CurrentRoom.CurrentSong == null || user.CurrentRoom.Reported)
			return null;

		string Name = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.Name));
		string Artist = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.Artist));
		string SubTitle = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.SubTitle));

		Hashtable song = Data.GetSong(Name, Artist, SubTitle);

		if (user.ShadowBanned)
		{
			if (song != null)
			{
				return song;
			}
			else
			{
				Hashtable ret = new Hashtable();
				ret["ID"] = -1;
				ret["Name"] = Name;
				ret["Artist"] = Artist;
				ret["SubTitle"] = SubTitle;
				ret["Played"] = 0;
				ret["Notes"] = 0;
				return ret;
			}
		}

		if (song == null)
		{
			MySql.Query("INSERT INTO songs (Name,Artist,SubTitle) VALUES(" +
						"'" + Name + "'," +
						"'" + Artist + "'," +
						"'" + SubTitle + "')");

			return MySql.Query("SELECT * FROM songs ORDER BY 'ID' DESC LIMIT 0,1")[0];
		}
		else if (Start)
		{
			MySql.Query("UPDATE songs SET Played=Played+1 WHERE ID=" + song["ID"].ToString());
			user.CurrentRoom.Reported = true;
		}

		return song;
	}

	public static string BanUser(User user, int originID)
	{
		string IP = user.tcpClient.Client.RemoteEndPoint.ToString().Split(':')[0];
		MySql.Query("INSERT INTO bans (IP,From) VALUES(\'" + IP + "\'," + originID + ")");
		return IP;
	}

	public static bool IsBanned(string IP)
	{
		Hashtable[] res = MySql.Query("SELECT * FROM bans WHERE IP = \'" + MySql.AddSlashes(IP) + "\'");
		return res.Length != 0;
	}

	public static void AddStats(User user)
	{
		if (user.CurrentRoom == null)
			return;

		if (user.CurrentRoom.CurrentSong == null)
			return;

		string Name = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.Name));
		string Artist = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.Artist));
		string SubTitle = MySql.AddSlashes(OpenSMO.User.Utf8Decode(user.CurrentRoom.CurrentSong.SubTitle));

		int songID = 0;
		Hashtable song = Data.GetSong(Name, Artist, SubTitle);

		if ( song == null )
		{
			MySql.Query("INSERT INTO songs (Name,Artist,SubTitle) VALUES(" +
						"'" + Name + "'," +
						"'" + Artist + "'," +
						"'" + SubTitle + "')");
		}
		Hashtable newsong = Data.GetSong(Name, Artist, SubTitle);


		if (newsong != null)
		{
			if (!user.ShadowBanned)
			{
				double songTime = user.SongTime.Elapsed.TotalSeconds;
				if (songTime != 0)
				{
					if ( (user.SongOptions.Contains("Fail")) && (user.User_Protocol == 2) )
					{
//                        if (songTime > (int)newsong["Time"])
//		        {
						MySql.Query("UPDATE songs SET Time=" + songTime.ToString().Replace(',', '.') + " WHERE ID=" + newsong["ID"]);
					}
//			}
				}
			}
		}



//		MainClass.AddLog("User ID  " +user.User_Table["ID"].ToString()  + "'s Final Timing: " + user.timing);
		MainClass.AddLog("User ID  " +user.User_Table["ID"].ToString()  + "'s Toasty Count: " + user.toasty);
		MainClass.AddLog("User ID  " +user.User_Table["ID"].ToString()  + "'s Room ID: " + user.CurrentRoom.roomid);
		// Give player XP
		int XP = 0;
		for (int i = 3; i <= 8; i++)
			XP += (i - 3) * user.Notes[i];
//
		MainClass.AddLog("Regular XP: " + XP + "Jump XP: " + user.jumpxp);
		XP += user.jumpxp;
		user.jumpxp= 0;
		XP /= 6;
		if ( user.timing > 2 )
		{
			user.toasty = 0;
			XP = 0;
		}
//            user.SendChatMessage("You gained " + Func.ChatColor("aaaa00") + XP.ToString() + Func.ChatColor("ffffff") + " XP!");
		int toastyxp = (user.toasty * 50);
		XP += toastyxp;

//		user.SendChatMessage("You gained an additional " + Func.ChatColor("aaaa00") + toastyxp.ToString() + Func.ChatColor("ffffff") + " Bonus XP for " + user.toasty + " Toasty(s) for a total of " + Func.ChatColor("aaaa00") + XP.ToString() + Func.ChatColor("ffffff") + " XP!");

		int fullcomboxp = 0;
		int marv = user.Notes[8];
		int perf = user.Notes[7];
		int grea = user.Notes[6];
		int good = user.Notes[5];
		int boo  = user.Notes[4];
		int miss = user.Notes[3];
		int ok   = user.Notes[10];
		int ng   = user.Notes[9];

		float Tpnt = (3 * marv) + (2 * perf) + grea - (4 * boo) - (8 * miss) + (6 * ok);
		float Tmaxpnt = 3 * (marv + perf + grea + good + boo + miss) + 6 * (ok + ng);
		float percentf = (Tpnt/Tmaxpnt)*100F;
		string mpercent = percentf.ToString("n3");
		string percent = percentf.ToString("n2");

		if ((miss == 0) && (boo == 0) && (good == 0) && ((marv + perf + grea) > 8))
		{
			if  ((marv + perf + grea) > 150)
			{
				fullcomboxp = 100;
			}
		}

		XP += fullcomboxp;
		int pretoastyxp = XP;


		string percentageq = "round(100.00/(3 * (Note_Flawless + Note_Perfect + Note_Great + Note_Good + Note_Barely + Note_Miss) + 6 * (Note_Held + Note_NG))*((3 * Note_Flawless) + (2 * Note_Perfect) + Note_Great - (4 * Note_Barely) - (8 * Note_Miss) + (6 * Note_Held)),3)";

		if (newsong != null)
		{
			songID = (int)newsong["ID"];

			Hashtable[] smoPBestQuery = MySql.Query("select count(*) as 'count' from stats where song = " + songID.ToString() + " and user = " + user.User_Table["ID"].ToString() + " and Difficulty = '" + ((int)user.GameDifficulty).ToString() + "' and Feet = '" + user.GameFeet.ToString() + "'  and " + percentageq +" > '" + mpercent  + "'");
			Hashtable PBStats = smoPBestQuery[0];
			int count = (int)PBStats["count"];
			count += 1;

			Hashtable[] smoPBestTotalQuery = MySql.Query("select count(*) as 'count' from stats where song = " + songID.ToString() + " and user = " + user.User_Table["ID"].ToString() + " and Difficulty = '" + ((int)user.GameDifficulty).ToString() + "' and Feet = '" + user.GameFeet.ToString() + "'");
			Hashtable oPBStats = smoPBestTotalQuery[0];
			int ocount = (int)oPBStats["count"];
			ocount += 1;

			Hashtable[] smoTBestQuery = MySql.Query("select count(*) as 'count' from stats where song = " + songID.ToString() + " and Difficulty = '" + ((int)user.GameDifficulty).ToString() + "' and Feet = '" + user.GameFeet.ToString() + "'  and " + percentageq +" > '" + mpercent  + "'");
			Hashtable TBStats = smoTBestQuery[0];
			int tcount = (int)TBStats["count"];
			tcount += 1;

			Hashtable[] smoTBestTotalQuery = MySql.Query("select count(*) as 'count' from stats where song = " + songID.ToString() + " and Difficulty = '" + ((int)user.GameDifficulty).ToString() + "' and Feet = '" + user.GameFeet.ToString() + "'");
			Hashtable oTBStats = smoTBestTotalQuery[0];
			int tocount = (int)oTBStats["count"];
			tocount += 1;
			string bestmessage= Func.ChatColor("aaaa00") + percent + "%" + Func.ChatColor("ffffff") + " PB: #"+ Func.ChatColor("aaaa00") + count + "/" + ocount + Func.ChatColor("ffffff") +" TB: #" + Func.ChatColor("aaaa00") + tcount + "/" + tocount + Func.ChatColor("ffffff");
			if (user.toasty > 0)
			{
				bestmessage = bestmessage + " " + Func.ChatColor("aaaa00") + pretoastyxp + "+" + toastyxp + Func.ChatColor("ffffff") + " XP Gained  - " + Func.ChatColor("aaaa00") + user.toasty + Func.ChatColor("ffffff") + " Toasty(s)";
			}
			else
			{
				bestmessage = bestmessage + " " + Func.ChatColor("aaaa00") + pretoastyxp + Func.ChatColor("ffffff") + " XP Gained ";
			}

			if ( fullcomboxp > 0 )
			{
				bestmessage = bestmessage + " -FC";
			}
			fullcomboxp = 0;

			if ( user.timing > 2 )
			{
				bestmessage = bestmessage + " -TIMING";
			}

			if (user.ShowOffset)
			{
				double clientoffsetavg = user.clientoffset / (double)user.clientoffsetcount;
				double clientoffsetms = clientoffsetavg * (double)1000;

				bestmessage = bestmessage + " " + Func.ChatColor("aa0000") + "+" + user.offsetpos.ToString() + Func.ChatColor("ffffff") + "/" + Func.ChatColor("aa0000") + "-" + user.offsetneg.ToString() + " " + clientoffsetms.ToString("n3") + Func.ChatColor("ffffff") + "ms";
			}


			string playerSettings = Sql.AddSlashes(user.GamePlayerSettings);

			if (!user.ShadowBanned)
			{

				// Big-ass query right there...
				if (!user.ShadowBanned)
				{
					MySql.Query("INSERT INTO stats (User,PlayerSettings,Song,Room,Feet,Difficulty,Grade,Score,MaxCombo," +
								"Note_0,Note_1,Note_Mine,Note_Miss,Note_Barely,Note_Good,Note_Great,Note_Perfect,Note_Flawless,Note_NG,Note_Held,Toasty,timing, rate) VALUES(" +
								user.User_Table["ID"].ToString() + ",'" + playerSettings + "'," + songID.ToString() + "," + user.CurrentRoom.roomid.ToString() + "," + user.GameFeet.ToString() + "," + ((int)user.GameDifficulty).ToString() + "," + ((int)user.Grade).ToString() + "," + user.Score.ToString() + "," + user.MaxCombo.ToString() + "," +
								user.Notes[0].ToString() + "," + user.Notes[1].ToString() + "," + user.Notes[2].ToString() + "," + user.Notes[3].ToString() + "," + user.Notes[4].ToString() + "," + user.Notes[5].ToString() + "," + user.Notes[6].ToString() + "," + user.Notes[7].ToString() + "," + user.Notes[8].ToString() + "," + user.Notes[9].ToString() + "," + user.Notes[10].ToString() + "," + user.toasty + "," + user.timing + "," + user.PlayerRate + ")");
				}
			}



			user.SendRoomChatMessage(bestmessage);

			user.toasty = 0;
			user.timing = 0;
			user.offsetneg = 0;
			user.offsetpos = 0;
			user.clientoffsetcount= 0;
			user.clientoffset= 0;
			user.servcombo=0;
			user.perfmarv=0;
		}
		if (!user.ShadowBanned)
			MySql.Query("UPDATE users SET XP=XP+" + XP.ToString() + " WHERE ID=" + user.User_ID.ToString());

		//Update Current rank in users name
		Hashtable[] checkxp = MySql.Query("select XP from users where ID = '" + user.User_Table["ID"].ToString() + "'");
		Hashtable XP_Table = checkxp[0];
		int currentxp = (int)XP_Table["XP"] + 1;

		Hashtable[] checkrank = MySql.Query("select count(*) as 'levelrank' from users where xp > '" + currentxp.ToString() + "'");
		Hashtable New_Rank_Table =  checkrank[0];
		int oldrank = user.User_Level_Rank;
		user.User_Level_Rank = (int)New_Rank_Table["levelrank"] + 1;
		if ( oldrank != user.User_Level_Rank )
		{
			user.SendChatMessage("Your Rank changed from " + Func.ChatColor("aaaa00") + oldrank.ToString() + Func.ChatColor("ffffff") + " to " + Func.ChatColor("aaaa00") + user.User_Level_Rank.ToString() + Func.ChatColor("ffffff") + " -- Congratz!");
		}

	}
}
}
