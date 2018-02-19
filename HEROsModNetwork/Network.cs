﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.Localization;

namespace HEROsMod.HEROsModNetwork
{
	public class Network
	{
		public static int NetworkVersion = 2;
		public static bool ServerUsingHEROsMod { get; set; }
		public static NetworkMode NetworkMode { get { return ModUtils.NetworkMode; } }
		public static bool LoggedIn { get; set; }

		public static List<Group> Groups { get; set; }
		public static HEROsModPlayer[] Players { get; set; }
		public static List<Region> Regions { get; set; }
		public static List<UserWithID> RegisteredUsers { get; set; }
		public static bool GravestonesAllowed { get; set; }
		public static bool WillFreezeNonLoggedIn { get; set; }
		public static int[,] TileLastChangedBy { get; set; }
		public static HEROsModPlayer LastTileKilledBy { get; set; }
		public static MemoryStream memoryStream;
		public static BinaryWriter writer;
		public static Group DefaultGroup;
		public static Group AdminGroup;

		//public static Group CTFGroup;
		public static int AuthCode;

		private static Color[] chatColor = new Color[]{
			Color.LightBlue,
			Color.LightCoral,
			Color.LightCyan,
			Color.LightGoldenrodYellow,
			Color.LightGray,
			Color.LightPink,
			Color.LightSkyBlue,
			Color.LightYellow
		};

		private static int chatColorIndex = 0;

		private static float authMessageTimer = 0f;
		private static float freezeTimer = 0f;
		private static float sendTimeTimer = 1f;

		public const string HEROsModCheckMessage = "-Install HEROs Mod For Advanced Features.  Type /login to login.  Type /register to register an account.";

		//public static byte HEROsModNetworkMessageType
		//{
		//    get { return (byte)(Main.maxMsg - 1); }
		//}

		public static void InitializeWorld()
		{
			if (NetworkMode == NetworkMode.Server)
			{
				TileLastChangedBy = new int[Main.maxTilesX, Main.maxTilesY];
				for (int x = 0; x < TileLastChangedBy.GetLength(0); x++)
				{
					for (int y = 0; y < TileLastChangedBy.GetLength(1); y++)
					{
						TileLastChangedBy[x, y] = -1;
					}
				}

				TileChangeController.Init();
				Groups = DatabaseController.GetGroups();
				Regions = DatabaseController.GetRegions();
			}
		}

		// On Load Mod
		public static void Init()
		{
			// Reset Values to defaults.
			Group.PermissionList.Clear();
			foreach (var item in Group.DefaultPermissions)
			{
				Group.PermissionList.Add(item);
			}

			ServerUsingHEROsMod = false;
			GravestonesAllowed = true;
			WillFreezeNonLoggedIn = true;
			Groups = new List<Group>();
			Players = new HEROsModPlayer[255];
			RegisteredUsers = new List<UserWithID>();
			for (int i = 0; i < Players.Length; i++)
			{
				Players[i] = new HEROsModPlayer(i);
			}
			Regions = new List<Region>();
			ResetWriter();
			LoggedIn = false;

			AdminGroup = new Group("Admin");
			AdminGroup.MakeAdmin();

			DatabaseController.Init();

			if (NetworkMode == NetworkMode.Server)
			{
				//TileLastChangedBy = new int[Main.maxTilesX, Main.maxTilesY];
				//for (int x = 0; x < TileLastChangedBy.GetLength(0); x++)
				//{
				//	for (int y = 0; y < TileLastChangedBy.GetLength(1); y++)
				//	{
				//		TileLastChangedBy[x, y] = -1;
				//	}
				//}

				//TileChangeController.Init();
				Groups = DatabaseController.GetGroups();
				//Regions = DatabaseController.GetRegions();
				//CTFGroup = new Group("CTFGroup");
				//CTFGroup.Permissions["StartCTF"] = true;

				foreach (Group group in Groups)
				{
					if (group.Name == "Default")
					{
						DefaultGroup = group;
						break;
					}
				}
				LoginService.GroupChanged += LoginService_GroupChanged;

				AuthCode = Main.rand.Next(100000, 999999);
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine(string.Format(HEROsMod.HeroText("DedicatedServerAutoMessage"), AuthCode));
				Console.ResetColor();
			}
		}

		public static void Update()
		{
			if (NetworkMode == NetworkMode.Server)
			{
				ModUtils.SetDeltaTime();
				//ErrorLogger.Log("Network.Update");
				//Console.WriteLine("Network.Update");
				freezeTimer -= ModUtils.DeltaTime;
				if (freezeTimer <= 0)
				{
					freezeTimer = 1f;
					//Console.WriteLine("FreezeNonLoggedInPlayers");

					FreezeNonLoggedInPlayers();
				}
				//authMessageTimer -= ModUtils.DeltaTime;
				//if (authMessageTimer <= 0)
				//{
				//	authMessageTimer = 60f;
				//
				//	Console.ForegroundColor = ConsoleColor.Yellow;
				//	Console.WriteLine("Create an account, login, and type /auth " + AuthCode + " to become Admin.");
				//	Console.ResetColor();
				//}
				if (HEROsModServices.TimeWeatherChanger.TimePaused)
				{
					Main.time = HEROsModServices.TimeWeatherChanger.PausedTime;
					if (sendTimeTimer > 0)
					{
						sendTimeTimer -= ModUtils.DeltaTime;
						if (sendTimeTimer <= 0)
						{
							sendTimeTimer = 1f;
							NetMessage.SendData(7, -1, -1, null, 0, 0f, 0f, 0f, 0);
						}
					}
				}
			}
		}

		private static void LoginService_GroupChanged(object sender, EventArgs e)
		{
			//Send group list to all HEROsMod users
			LoginService.SendGroupList(-2);
		}

		public static void ResetWriter()
		{
			if (memoryStream != null)
			{
				memoryStream.Close();
			}
			memoryStream = new MemoryStream();
			writer = new BinaryWriter(memoryStream);
		}

		public static bool PlayerHasPermissionToBuildAtBlock(HEROsModPlayer player, int x, int y)
		{
			bool canBuild = false;

			if (player.Group.IsAdmin/* && !CTF.CaptureTheFlag.GameInProgress*/)
			{
				canBuild = true;
			}

			if (!canBuild /*&& !CTF.CaptureTheFlag.GameInProgress*/ && player.Group.HasPermission("ModifyTerrain"))
			{
				canBuild = true;
				for (int i = 0; i < Regions.Count; i++)
				{
					//if region contains tile
					if (Regions[i].ContainsTile(x, y))
					{
						bool canBuildInRegion = false;
						for (int j = 0; j < Regions[i].AllowedGroupsIDs.Count; j++)
						{
							if (player.Group.ID == Regions[i].AllowedGroupsIDs[j])
							{
								//can build in region
								canBuildInRegion = true;
								break;
							}
						}
						// if can't build in region chack if player can build in the region
						if (!canBuildInRegion)
						{
							for (int j = 0; j < Regions[i].AllowedPlayersIDs.Count; j++)
							{
								if (player.ID == Regions[i].AllowedPlayersIDs[j])
								{
									canBuildInRegion = true;
									break;
								}
							}
						}
						canBuild = canBuildInRegion;
						if (!canBuild)
							break;
					}
				}
			}
			return canBuild;
		}

		// TODO -- How will any of these work....?
		public static bool CheckIncomingDataForHEROsModMessage(ref byte msgType, ref BinaryReader binaryReader, int playerNumber)
		{
			long readerPos = binaryReader.BaseStream.Position;

			switch (msgType)
			{
				case 12:
					if (NetworkMode == NetworkMode.Server)
					{
						//if (CTF.CaptureTheFlag.GameInProgress && Netplay.Clients[playerNumber].State == 10)
						//{
						//	if (Players[playerNumber].CTFTeam != CTF.TeamColor.None)
						//	{
						//		CTF.CTFMessages.SendPlayerToSpawnPlatform(Players[playerNumber]);
						//		return true;
						//	}

						//}
						if (Netplay.Clients[playerNumber].State == 3)
						{
							PlayerJoined(playerNumber);
						}
					}
					break;
				//case 14:
				//	if (NetworkMode != global::HEROsModMod.NetworkMode.Server)
				//	{
				//		if (CTF.CaptureTheFlag.GameInProgress)
				//		{
				//			int index = (int)binaryReader.ReadByte();
				//			byte active = binaryReader.ReadByte();
				//			if (Players[index].CTFTeam == CTF.TeamColor.None && active == 1 && Players[index].GameInstance.ghost)
				//			{
				//				return true;
				//			}
				//		}
				//	}
				//	break;
				case 17: //Terrain Modified
					if (NetworkMode == NetworkMode.Server)
					{
						bool canBuild = false;
						TileModifyType tileModifyType = (TileModifyType)binaryReader.ReadByte();
						int x = (int)binaryReader.ReadInt16();
						int y = (int)binaryReader.ReadInt16();
						short placeType = binaryReader.ReadInt16();
						int style = (int)binaryReader.ReadByte();
						bool fail = placeType == 1;
						HEROsModPlayer player = Players[playerNumber];

						Tile tile;
						if (x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY)
						{
							tile = Main.tile[x, y];
						}
						else
						{
							binaryReader.BaseStream.Position = readerPos;
							return false;
						}

						//if (CTF.CaptureTheFlag.GameInProgress && player.CTFTeam != CTF.TeamColor.None && CTF.CaptureTheFlag.AllowTerrainModification)
						//{
						//	canBuild = true;
						//	if (CTF.CaptureTheFlag.ListeningForTileChanges)
						//	{
						//		Tile backupTile = CTF.CaptureTheFlag.ModifiedTiles[x, y];
						//		if (backupTile == null)
						//		{
						//			CTF.CaptureTheFlag.ModifiedTiles[x, y] = new Tile();
						//			CTF.CaptureTheFlag.ModifiedTiles[x, y].CopyFrom(tile);
						//			Console.WriteLine("tile added");
						//		}
						//	}
						//}
						if (!canBuild)
						{
							canBuild = PlayerHasPermissionToBuildAtBlock(player, x, y);
						}

						if (tileModifyType == TileModifyType.PlaceTile && placeType == TileID.LandMine)
						{
							SendTextToPlayer("Landmines are disabled on this server", playerNumber, Color.Red);
						}
						else if (canBuild)
						{
							TileLastChangedBy[x, y] = player.ID;
							binaryReader.BaseStream.Position = readerPos;
							if (tileModifyType == TileModifyType.KillTile)
							{
								LastTileKilledBy = player;
								WorldGen.KillTile(x, y, fail, false, false);
								NetMessage.SendData(17, -1, playerNumber, null, (int)tileModifyType, (float)x, (float)y, (float)placeType, style);
								LastTileKilledBy = null;
								return true;
							}
							else
							{
								TileChangeController.RecordChanges(player, x, y);
							}
							return false;
						}
						else
						{
							SendTextToPlayer(HEROsMod.HeroText("YouDoNotHavePermissionToBuildHere"), playerNumber, Color.Red);
						}

						switch (tileModifyType)
						{
							case TileModifyType.KillTile:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceTile, (float)x, (float)y, (float)tile.type, tile.slope());
								break;

							case TileModifyType.PlaceTile:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillTile, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillWall:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceWall, (float)x, (float)y, (float)tile.wall, style);
								break;

							case TileModifyType.PlaceWall:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillWall, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillTileNoItem:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceTile, (float)x, (float)y, (float)tile.type, tile.slope());
								break;

							case TileModifyType.PlaceWire:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillWire, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.PlaceWire2:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillWire2, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.PlaceWire3:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillWire3, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillWire:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceWire, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillWire2:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceWire2, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillWire3:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceWire3, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.KillActuator:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PlaceActuator, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.PlaceActuator:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.KillActuator, (float)x, (float)y, (float)placeType, style);
								break;

							case TileModifyType.PoundTile:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.PoundTile, (float)x, (float)y, (float)placeType, tile.slope());
								break;

							case TileModifyType.SlopeTile:
								NetMessage.SendData(17, playerNumber, -1, null, (int)TileModifyType.SlopeTile, (float)x, (float)y, (float)placeType, tile.slope());
								break;
						}
						return true;
					}
					break;
				/*				case 25: //received a chat message

									binaryReader.ReadByte();
									Color color = binaryReader.ReadRGB();
									string text = binaryReader.ReadString();
									if (NetworkMode == NetworkMode.Client)
									{
										if (text == HEROsModCheckMessage)
										{
											ServerUsingHEROsMod = true;
											HEROsMod.ServiceHotbar.Visible = true;
											GeneralMessages.TellSereverImUsingHEROsMod();
											return true;
										}
									}
									else if (NetworkMode == NetworkMode.Server)
									{
										if (text.Length > 1 && text.Substring(0, 1) == "/")
										{
											string[] parameters = text.Substring(1, text.Length - 1).Split(' ');
											parameters[0] = parameters[0].ToLower();
											switch (parameters[0])
											{
												case "auth":
													if (parameters.Length != 2 || parameters[1].Length != 6)
													{
														SendTextToPlayer("Invalid Input", playerNumber);
														return true;
													}
													string authStr = parameters[1];
													if (authStr == AuthCode.ToString())
													{
														if (!Players[playerNumber].UsingHEROsMod)
														{
															SendTextToPlayer("You need HEROsMod Mod to use this feature", playerNumber);
															return true;
														}
														if (Players[playerNumber].Username.Length > 0)
														{
															Players[playerNumber].Group = AdminGroup;
															DatabaseController.SetPlayerGroup(Players[playerNumber].ID, Players[playerNumber].Group.ID);
															LoginService.SendPlayerPermissions(playerNumber);
															SendTextToPlayer("You are now Admin", playerNumber);
															return true;
														}
														else
														{
															SendTextToPlayer("Please login first", playerNumber);
															return true;
														}
													}
													else
													{
														SendTextToPlayer("Invalid Auth Code", playerNumber);
														return true;
													}
												case "login":
													if (parameters.Length != 3)
													{
														SendTextToPlayer("Invalid Input", playerNumber);
														return true;
													}
													LoginService.ProcessLoginRequest(parameters[1], parameters[2], playerNumber);
													return true;

												case "logout":
													if (parameters.Length != 1)
													{
														SendTextToPlayer("Invalid Input", playerNumber);
														return true;
													}
													LoginService.ProcessLogoutRequest(playerNumber);
													return true;

												case "register":
													if (parameters.Length != 3)
													{
														SendTextToPlayer("Invalid Input", playerNumber);
														return true;
													}
													LoginService.ProcessRegistrationRequest(parameters[1], parameters[2], playerNumber);
													break;

												case "help":
													if (parameters.Length != 1)
													{
														SendTextToPlayer("Invalid Input", playerNumber);
													}
													SendTextToPlayer("/login <username> <password> - Login with your account", playerNumber);
													SendTextToPlayer("/register <username> <password> - Create an account", playerNumber);
													SendTextToPlayer("/logout - Logout of your account", playerNumber);
													SendTextToPlayer("Use HEROsMod Mod to unlock all server features.", playerNumber);
													break;

												default:
													SendTextToPlayer("Invalid Command, type /help for a list of commands.", playerNumber);
													break;
											}
										}
										else
										{
											string text2 = text.ToLower();
											if (text2 == Lang.mp[6] || text2 == Lang.mp[21])
											{
												string text3 = "";
												for (int i = 0; i < 255; i++)
												{
													if (Main.player[i].active)
													{
														if (text3 == "")
														{
															text3 = Main.player[i].name;
														}
														else
														{
															text3 = text3 + ", " + Main.player[i].name;
														}
													}
												}
												NetMessage.SendData(25, playerNumber, -1, Lang.mp[7] + " " + text3 + ".", 255, 255f, 240f, 20f, 0);
											}
											else if (text2.StartsWith("/me "))
											{
												NetMessage.SendData(25, -1, -1, "*" + Main.player[playerNumber].name + " " + text.Substring(4), 255, 200f, 100f, 0f, 0);
											}
											else if (text2 == Lang.mp[8])
											{
												NetMessage.SendData(25, -1, -1, string.Concat(new object[]
											{
												"*",
												Main.player[playerNumber].name,
												" ",
												Lang.mp[9],
												" ",
												Main.rand.Next(1, 101)
											}), 255, 255f, 240f, 20f, 0);
											}
											else if (text2.StartsWith("/p "))
											{
												int num28 = Main.player[playerNumber].team;
												color = Main.teamColor[num28];
												if (num28 != 0)
												{
													for (int i = 0; i < 255; i++)
													{
														if (Main.player[i].team == num28)
														{
															NetMessage.SendData(25, i, -1, text.Substring(3), 255, (float)color.R, (float)color.G, (float)color.B, 0);
														}
													}
												}
												else
												{
													NetMessage.SendData(25, playerNumber, -1, Lang.mp[10].ToNetworkText(), 255, 255f, 240f, 20f, 0);
												}
											}
											else
											{
												return false;
												// why are chat messages randomized?
												//color = chatColor[chatColorIndex];
												//chatColorIndex++;
												//if (chatColorIndex >= chatColor.Length) chatColorIndex = 0;
												//NetMessage.SendData(25, -1, -1, text, 255, (float)color.R, (float)color.G, (float)color.B, 0);
												//if (Main.dedServ)
												//{
												//	Console.WriteLine("<" + Main.player[playerNumber].name + "> " + text);
												//}
											}
										}
										return true;
									}
									break;*/
				//case 27:
				//	if (ItemBanner.ItemsBanned && !Players[playerNumber].Group.IsAdmin)
				//	{
				//		int projIdentity = (int)binaryReader.ReadInt16();
				//		Vector2 position = binaryReader.ReadVector2();
				//		Vector2 velocity = binaryReader.ReadVector2();
				//		float knockback = binaryReader.ReadSingle();
				//		int damage = (int)binaryReader.ReadInt16();
				//		int owner = (int)binaryReader.ReadByte();
				//		int type = (int)binaryReader.ReadInt16();

				//		Console.WriteLine("Prof: " + type);
				//		int[] bannedProjectiles = ItemBanner.bannedProjectiles;
				//		for (int i = 0; i < bannedProjectiles.Length; i++)
				//		{
				//			if (bannedProjectiles[i] == type)
				//			{
				//				Projectile newProj = new Projectile();
				//				newProj.SetDefaults(type);
				//				SendTextToPlayer(newProj.name + " is banned on the server", playerNumber, Color.Red);

				//				int projIndex = 0;
				//				for (int j = 0; j < 1000; j++)
				//				{
				//					if (!Main.projectile[j].active)
				//					{
				//						Projectile proj = Main.projectile[j];
				//						proj.owner = owner;
				//						projIndex = j;
				//						break;
				//					}
				//				}

				//				NetMessage.SendData(27, playerNumber, -1, "", projIndex);
				//				NetMessage.SendData(29, playerNumber, -1, "", projIdentity, (float)owner);
				//				return true;
				//			}
				//		}
				//	}
				//	break;
				//case 30:
				//	if (NetworkMode == global::HEROsModMod.NetworkMode.Server)
				//	{
				//		if (CTF.CaptureTheFlag.GameInProgress)
				//		{
				//			SendTextToPlayer("You cannot change your hostility while Capture the Flag is in progress.", playerNumber);
				//			CTF.CaptureTheFlag.SetPlayerHostility(Players[playerNumber]);
				//			return true;
				//		}
				//	}
				//	break;
				//case 45:
				//	if (NetworkMode == global::HEROsModMod.NetworkMode.Server)
				//	{
				//		if (CTF.CaptureTheFlag.GameInProgress)
				//		{
				//			SendTextToPlayer("You cannot change parties while Capture the Flag is in progress.", playerNumber);
				//			CTF.CaptureTheFlag.SetPlayerHostility(Players[playerNumber]);
				//			return true;
				//		}
				//	}
				//	break;
				case 63: //block painted
					if (NetworkMode == global::HEROsMod.NetworkMode.Server)
					{
						int x = (int)binaryReader.ReadInt16();
						int y = (int)binaryReader.ReadInt16();
						byte paintColor = binaryReader.ReadByte();
						HEROsModPlayer player = Players[playerNumber];

						if (PlayerHasPermissionToBuildAtBlock(player, x, y))
						{
							TileLastChangedBy[x, y] = player.ID;
							binaryReader.BaseStream.Position = readerPos;
							return false;
						}
						else
						{
							NetMessage.SendData(63, playerNumber, -1, null, x, (float)y, (float)Main.tile[x, y].color());
							SendTextToPlayer(HEROsMod.HeroText("YouDoNotHavePermissionToBuildHere"), playerNumber, Color.Red);
							return true;
						}
					}
					break;

				case 64: //wall painted
					if (NetworkMode == global::HEROsMod.NetworkMode.Server)
					{
						int x = (int)binaryReader.ReadInt16();
						int y = (int)binaryReader.ReadInt16();
						byte paintColor = binaryReader.ReadByte();
						HEROsModPlayer player = Players[playerNumber];

						if (PlayerHasPermissionToBuildAtBlock(player, x, y))
						{
							TileLastChangedBy[x, y] = player.ID;
							binaryReader.BaseStream.Position = readerPos;
							return false;
						}
						else
						{
							NetMessage.SendData(64, playerNumber, -1, null, x, (float)y, (float)Main.tile[x, y].wallColor());
							SendTextToPlayer(HEROsMod.HeroText("YouDoNotHavePermissionToBuildHere"), playerNumber, Color.Red);
							return true;
						}
					}
					break;
			}

			//if (msgType == HEROsModNetworkMessageType)
			//{
			//    //We found a HEROsMod only message
			//    MessageType subMsgType = (MessageType)binaryReader.ReadByte();
			//    switch(subMsgType)
			//    {
			//        case MessageType.GeneralMessage:
			//            GeneralMessages.ProcessData(ref binaryReader, playerNumber);
			//            break;
			//        case MessageType.LoginMessage:
			//            LoginService.ProcessData(ref binaryReader, playerNumber);
			//            break;
			//        case MessageType.CTFMessage:
			//            CTF.CTFMessages.ProcessData(ref binaryReader, playerNumber);
			//            break;
			//    }
			//}

			//we need to set the stream position back to where it was before we got it
			binaryReader.BaseStream.Position = readerPos;
			return false;
		}

		public static void HEROsModMessaged(BinaryReader binaryReader, int playerNumber)
		{
			//if (msgType == HEROsModNetworkMessageType)
			{
				//We found a HEROsMod only message
				MessageType subMsgType = (MessageType)binaryReader.ReadByte();
				ModUtils.DebugText("subMsgType " + subMsgType);

				switch (subMsgType)
				{
					case MessageType.GeneralMessage:
						GeneralMessages.ProcessData(ref binaryReader, playerNumber);
						break;

					case MessageType.LoginMessage:
						LoginService.ProcessData(ref binaryReader, playerNumber);
						break;
						//case MessageType.CTFMessage:
						//	CTF.CTFMessages.ProcessData(ref binaryReader, playerNumber);
						//	break;
				}
			}
		}

		//public static bool SendDataCheck(int msgType, int number)
		//{
		//	switch (msgType)
		//	{
		//		case 27: //projectiles
		//			if (!GravestonesAllowed)
		//			{
		//				Projectile proj = Main.projectile[number];
		//				if (proj.type == 43 || (proj.type > 200 && proj.type < 206))
		//				{
		//					proj.active = false;
		//					return true;
		//				}
		//			}
		//			break;
		//	}
		//	return false;
		//}

		private static void PlayerJoined(int playerNumber)
		{
			Players[playerNumber] = new HEROsModPlayer(playerNumber);
			// chat message hack: SendTextToPlayer(HEROsModCheckMessage, playerNumber, Color.Red);

			var packet = HEROsMod.instance.GetPacket();
			packet.Write((byte)MessageType.LoginMessage);
			packet.Write((byte)LoginService.MessageType.ServerToClientHandshake);
			packet.Send(playerNumber);

			GeneralMessages.TellClientsPlayerJoined(playerNumber);
		}

		public static void PlayerLeft(int playerIndex)
		{
			Players[playerIndex].Reset();
			//if (CTF.CaptureTheFlag.GameInProgress || CTF.CaptureTheFlag.InPregameLobby)
			//{
			//	if (playerIndex == CTF.CaptureTheFlag.LobbyStartedBy)
			//	{
			//		CTF.CaptureTheFlag.EndGame();
			//	}
			//}
			GeneralMessages.TellClientsPlayerLeft(playerIndex);
		}

		private static void FreezeNonLoggedInPlayers()
		{
			for (int i = 0; i < Players.Length; i++)
			{
				HEROsModPlayer player = Players[i];
				if (player.ServerInstance.IsActive)
				{
					if (player.Username == string.Empty)
					{
						//player.GameInstance.AddBuff(47, 7200);
						//	Console.WriteLine("Freeze " + i);
						NetMessage.SendData(55, player.Index, -1, null, player.Index, 47, 120, 0f, 0);
					}
				}
			}
		}

		public static void SendPlayerToPosition(HEROsModPlayer player, Vector2 position)
		{
			position /= 16;
			int prevSpawnX = player.GameInstance.SpawnX;
			int prevSpawnY = player.GameInstance.SpawnY;
			player.GameInstance.SpawnX = (int)position.X;
			player.GameInstance.SpawnY = (int)position.Y;
			NetMessage.SendData(12, -1, -1, null, player.Index, 0f, 0f, 0f, 0);
			player.GameInstance.SpawnX = prevSpawnX;
			player.GameInstance.SpawnY = prevSpawnY;
		}

		public static void SendTextToPlayer(string msg, int playerIndex, Color? color = null)
		{
			Color c = color.GetValueOrDefault(Color.White);
			//NetMessage.SendData(25, playerIndex, -1, msg, 255, c.R, c.G, c.B, 0);
			NetMessage.SendChatMessageToClient(NetworkText.FromLiteral(msg), c, playerIndex);
		}

		public static void SendTextToAllPlayers(string msg, Color? color = null)
		{
			Color c = color.GetValueOrDefault(Color.White);
			//NetMessage.SendData(25, -1, -1, msg, 255, c.R, c.G, c.B, 0);
			NetMessage.BroadcastChatMessage(NetworkText.FromLiteral(msg), c, -1);
		}

		public static void SendDataToServer()
		{
			ModUtils.DebugText("SendDataToServer " + memoryStream.ToArray());
			var a = HEROsMod.instance.GetPacket();
			a.Write(memoryStream.ToArray());
			a.Send();
			ResetWriter();
			//NetMessage.SendData(HEROsModNetworkMessageType);
		}

		public static void SendDataToPlayer(int playerNumber)
		{
			if (playerNumber == -2)
			{
				SendDataToAllHEROsModUsers();
			}
			else
			{
				var a = HEROsMod.instance.GetPacket();
				a.Write(memoryStream.ToArray());
				a.Send(playerNumber);
				ResetWriter();

				//NetMessage.SendData(HEROsModNetworkMessageType, playerNumber)
			}
		}

		public static void SendDataToAllHEROsModUsers()
		{
			//for(int i = 0;i < Players.Length; i++)
			//{
			//    byte[] bytes = memoryStream.ToArray();
			//    if(Players[i] != null && Players[i].UsingHEROsMod)
			//    {
			//        NetMessage.SendData(HEROsModNetworkMessageType, i);
			//        writer.Write(bytes);
			//    }
			//}
			for (int i = 0; i < Players.Length; i++)
			{
				byte[] bytes = memoryStream.ToArray();
				if (Players[i] != null && Players[i].UsingHEROsMod)
				{
					var a = HEROsMod.instance.GetPacket();
					a.Write(memoryStream.ToArray());
					a.Send(i);
					ResetWriter();
					//NetMessage.SendData(HEROsModNetworkMessageType, i);
					writer.Write(bytes);
				}
			}
			ResetWriter();
		}

		public static Group GetGroupByID(int id)
		{
			if (id == -1) return AdminGroup;
			for (int i = 0; i < Groups.Count; i++)
			{
				if (Groups[i].ID == id)
					return Groups[i];
			}
			return null;
		}

		public static Group GetGroupByName(string name)
		{
			if (name == AdminGroup.Name) return AdminGroup;
			for (int i = 0; i < Groups.Count; i++)
			{
				if (Groups[i].Name == name)
					return Groups[i];
			}
			return null;
		}

		public static Region GetRegionByID(int id)
		{
			for (int i = 0; i < Regions.Count; i++)
			{
				if (Regions[i].ID == id)
					return Regions[i];
			}
			return null;
		}

		public static void ClearGroundItems()
		{
			for (int i = 0; i < Main.item.Length; i++)
			{
				if (Main.item[i].active)
				{
					Main.item[i].SetDefaults(0);
					NetMessage.SendData(21, -1, -1, null, i, 0f, 0f, 0f, 0);
				}
			}
		}

		public static void SpawnNPC(int type, Vector2 position)
		{
			bool npcFound = false;
			for (int i = 0; i < Main.npc.Length; i++)
			{
				NPC n = Main.npc[i];
				if (n.type == type)
				{
					n.position = position;
					npcFound = true;
					if (Main.netMode == 2) NetMessage.SendData(23, -1, -1, null, i, 0f, 0f, 0f, 0);
					break;
				}
			}
			if (!npcFound) NPC.NewNPC((int)position.X, (int)position.Y, type);
		}

		public static void ResetAllPlayers()
		{
			for (int i = 0; i < Players.Length; i++)
			{
				Players[i].Reset();
			}
		}

		public static void ResendPlayerTileData(HEROsModPlayer player)
		{
			int sectionX = Netplay.GetSectionX((int)(player.GameInstance.position.X / 16f));
			int sectionY = Netplay.GetSectionY((int)(player.GameInstance.position.Y / 16f));

			int num = 0;
			for (int i = sectionX - 1; i < sectionX + 2; i++)
			{
				for (int j = sectionY - 1; j < sectionY + 2; j++)
				{
					if (i >= 0 && i < Main.maxSectionsX && j >= 0 && j < Main.maxSectionsY)
					{
						num++;
					}
				}
			}
			int num2 = num;
			NetMessage.SendData(9, player.Index, -1, Lang.inter[44].ToNetworkText(), num2, 0f, 0f, 0f, 0);
			Netplay.Clients[player.Index].StatusText2 = "is receiving tile data";
			Netplay.Clients[player.Index].StatusMax += num2;
			for (int k = sectionX - 1; k < sectionX + 2; k++)
			{
				for (int l = sectionY - 1; l < sectionY + 2; l++)
				{
					if (k >= 0 && k < Main.maxSectionsX && l >= 0 && l < Main.maxSectionsY)
					{
						NetMessage.SendSection(player.Index, k, l, false);
						NetMessage.SendData(11, player.Index, -1, null, k, (float)l, (float)k, (float)l, 0);
					}
				}
			}
		}

		public enum MessageType
		{
			GeneralMessage,
			LoginMessage,
			CTFMessage
		}

		public enum TileModifyType : byte
		{
			KillTile,
			PlaceTile,
			KillWall,
			PlaceWall,
			KillTileNoItem,
			PlaceWire,
			KillWire,
			PoundTile,
			PlaceActuator,
			KillActuator,
			PlaceWire2,
			KillWire2,
			PlaceWire3,
			KillWire3,
			SlopeTile,
			FrameTrack
		}
	}
}