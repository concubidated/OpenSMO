﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;

namespace OpenSMO {
    public class Sql {
        public static string Filename;
        public static int Version;
        public static bool Compress;

        private static SQLiteConnection conn;
        private static SQLiteCommand cmd;

        public static void Connect() {
            conn = new SQLiteConnection("Data Source=" + Filename + ";Version=" + Version.ToString() + ";New=False;Compress=" + Compress.ToString() + ";");
            try { conn.Open(); } catch (Exception ex) {
                MainClass.AddLog("Couldn't open SQLite database: " + ex.Message, true);
            }

            Query("BEGIN TRANSACTION");
        }

        public static bool Connected {
            get { return conn != null; }
        }

        public static string AddSlashes(string str) {
            return str.Replace("'", "''");
        }

        public static void Close() {
            conn.Close();
        }

        private static int commitTimer;
        public static void Update() {
            if (++commitTimer >= MainClass.Instance.FPS * int.Parse(MainClass.Instance.ServerConfig.Get("Database_CommitTime"))) {
                commitTimer = 0;
                Query("COMMIT TRANSACTION");
                Query("BEGIN TRANSACTION");
            }
        }

        public static Hashtable[] Query(string qry) {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            SQLiteDataReader reader = null;

            cmd = conn.CreateCommand();
            cmd.CommandText = qry;

            try { reader = cmd.ExecuteReader(); } catch (Exception ex) {
                MainClass.AddLog("Query error: '" + ex.Message + "'", true);
                MainClass.AddLog("Query was: '" + qry + "'", true);
                return null;
            }

            List<Hashtable> ret = new List<Hashtable>();

            while (reader.Read()) {
                Hashtable row = new Hashtable();

                for (int i = 0; i < reader.FieldCount; i++) {
                    if (reader[i].GetType() == typeof(Int64)) {
                        if ((long)reader[i] > int.MaxValue)
                            row[reader.GetName(i)] = (long)reader[i];
                        else
                            row[reader.GetName(i)] = (int)(long)reader[i];
                    } else
                        row[reader.GetName(i)] = reader[i];
                }

                ret.Add(row);
            }

            sw.Stop();

            if (sw.ElapsedMilliseconds >= 1000)
                MainClass.AddLog("SQL Query took very long: " + sw.ElapsedMilliseconds + "ms", true);

            return ret.ToArray();
        }
    }
}
