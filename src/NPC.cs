/*
Historically accurate educational video game based in 1830s Lausanne.
Copyright (C) 2021  GameLab UNIL-EPFL

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using Godot;
using System;
using System.Collections.Generic;

public class NPC : KinematicBody2D {
	
	public enum Direction {RIGHT, LEFT, UP, DOWN};
	private Vector2 RightDir = new Vector2(1.0f, 0.0f);
	private Vector2 LeftDir = new Vector2(-1.0f, 0.0f);
	private Vector2 UpDir = new Vector2(0.0f, -1.0f);
	private Vector2 DownDir = new Vector2(0.0f, 1.0f);
	
	private const int MAX_CHAR_PER_LINE = 35;
	private const int MAX_LINES = 3;
	
	private DialogueController DC;
	private QuestController QC;
	
	private string[] InnerLines;
	private int InnerLinesCount = 0;
	
	//Used to display text
	private TextBox TB;
	
	//For Idle animation starting
	private AnimationTree AT;
	private AnimationNodeStateMachinePlayback AS;
	private bool IdleAnimIsPlaying = false;
	
	//Randomness
	private Random random = new Random();
	
	//For wandering
	private bool isWandering = false;
	private Vector2 Velocity = Vector2.Zero;
	private Vector2 InputVec = Vector2.Zero;
	private const int ACC = 950;
	private const int FRIC = 1000;
	private float cooldown = 0.0f;
	private float wanderTime = 1.0f;
	private Vector2 NextDir = Vector2.Zero;
	
	private Vector2 InitialDirection = Vector2.Zero;
	
	[Export]
	public Direction InitDir = Direction.DOWN;
	[Export]
	public bool CanTurn = true;
	[Export]
	public int ProbRight = 2; //max weight of right movement
	[Export]
	public int ProbLeft = 2; //max weight of left movement
	[Export]
	public int ProbUp = 2; //max weight of up movement
	[Export]
	public int ProbDown = 2; //max weight of down movement
	[Export]
	public bool CanWander = false;
	[Export]
	public float WanderingCooldown = 5.0f;
	[Export]
	public float WanderingDist = 1.0f;
	[Export]
	public int WalkSpeed = 50;
	[Export]
	public int IdleStartProb = 25; //0 to 100
	[Export]
	public string AutoDialogueID;
	[Export]
	public string DemandDialogueID;
	[Export]
	public bool HasAutoDialogue = true;
	[Export]
	public bool HasDemandDialogue = true;
	[Export]
	public bool isQuestNPC = false;
	[Export]
	public bool isBrewer = false;
	[Export]
	public float BrewBadThreshold = 40f;
	[Export]
	public float BrewPerfectThreshold = 90f;
	[Export]
	public bool isTrueschel = false;
	
	private bool inDialogue = false;
	private bool inAutoDialogue = false;
	
	private Context context;

	private const string questFile = "/dialogues/xml/QuestNPC.xml";
	
	private string[] noms = {"Cerjeat", "Trüschel", "Perregaux", "Montelieu", "Mercier", "Rochat"};
	
	private string[] BrewQuestText() {
		//Check if the game has been played yet
		if(context._CheckBrewBurn() == -1.0f) {
			return DC._QueryDialogue("brewStart", "onDemand", questFile);
		} 
		// Check the brew score after the minigame and respond appropriately
		if(context._CheckBrewBurn() < BrewBadThreshold) {
			return DC._QueryDialogue("brewFail", "onDemand", questFile);
		} 
		if(context._CheckBrewBurn() < BrewPerfectThreshold) {
			return DC._QueryDialogue("brewGood", "onDemand", questFile);
		} else {
			return DC._QueryDialogue("brewPerfect", "onDemand", questFile);
		}
	}
	
	private string[] QuestText(InfoValue_t res) {
		string[] outliers = res.Outliers().ToArray();
		
		// Check the current state of the notebook and reply appropriately
		if(context._AllTabsCorrect()) {
			return DC._QueryDialogue("questTextAllCorrect", "onDemand", questFile);
		}
		if(res.IsCorrect()) {
			return FormatText(
				DC._QueryDialogue("questTextCorrect", "onDemand", questFile),
				1,
				new string[] { context._GetNotCorrectTabs().ToString() }
			);
		}
	
		// Check the number of outliers and request the right dialogue
		int i = outliers.Length;
		if(i > 1) {
			return FormatText(
				DC._QueryDialogue("questTextWrongPlural", "onDemand", questFile),
				2,
				new string[] { noms[context.CurrentTab], i.ToString() }
			);
		}
		return FormatText(
			DC._QueryDialogue("questTextWrongSingular", "onDemand", questFile),
			2,
			new string[] { noms[context.CurrentTab], i.ToString() }
		);
	}
	
	private void LookInInitalDir() {
		InputVec = InitialDirection;
		HandleMovement(0.03f);
		InputVec = Vector2.Zero;
		HandleMovement(0.03f);
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		Show();
		//Fetch the scene's Dialogue controller and the TextBox
		context = GetNode<Context>("/root/Context");
		DC = Owner.GetNode<DialogueController>("DialogueController");
		QC = Owner.GetNode<QuestController>("QuestController");
		TB = GetNode<TextBox>("TextBox");
		AT = GetNode<AnimationTree>("AnimationTree");
		AS = (AnimationNodeStateMachinePlayback)AT.Get("parameters/playback");
		
		//Sanity Check
		if(HasDemandDialogue || HasAutoDialogue) {
			if(DC == null) {
				throw new Exception("Every scene must have its own dialogue controller!!");
			} 
		}
		
		//Set initial direction
		switch(InitDir) {
			case Direction.LEFT:
				InitialDirection = LeftDir;
				break;
			case Direction.RIGHT:
				InitialDirection = RightDir;
				break;
			case Direction.UP:
				InitialDirection = UpDir;
				break;
			default:
			case Direction.DOWN:
				InitialDirection = DownDir;
				break;
		}
		LookInInitalDir();
	}
	
	public void _StopTalking() {
		if(!inDialogue) {
			TB._HideText();
		}
	}
	
	private void HandleMovement(float delta) {
		//Update velocity
		if(InputVec == Vector2.Zero) {
			Velocity = Velocity.MoveToward(Vector2.Zero, FRIC * delta);
		} else {
			//Set blend positions for animation
			AT.Set("parameters/Walk/blend_position", InputVec);
			AT.Set("parameters/Idle/blend_position", InputVec);
			Velocity = Velocity.MoveToward(InputVec * WalkSpeed, ACC * delta);
		}
	}
	
	//Generate a new random position within the wandering distance
	private Vector2 NewInputVec() {
		float horizontalMov = 0.0f;
		float verticalMov = 0.0f;
		
		//Check if a collision has happened since the last movement
		if(NextDir != Vector2.Zero) {
			//If so, move away from the collision
			horizontalMov = NextDir[0];
			verticalMov = NextDir[1];
			NextDir = Vector2.Zero;
		} else {
			horizontalMov = (float)(random.Next(ProbRight) - random.Next(ProbLeft));
			verticalMov = (float)(random.Next(ProbDown) - random.Next(ProbUp));
		}
		
		if((random.Next(2) > 0 || verticalMov == 0.0f) && horizontalMov != 0.0f) {
			return new Vector2(horizontalMov, 0.0f);
		}
		return new Vector2(0.0f, verticalMov);
	}
	
	private void StopWandering() {
		wanderTime = 0.0f;
		isWandering = false;
		InputVec = Vector2.Zero;
		Velocity = Vector2.Zero;
		AS.Travel("Idle");
		
		//Start cooldown
		cooldown = (random.Next(100)/100.0f) * WanderingCooldown;
	}
	
	public override void _Process(float delta) {
		if(!IdleAnimIsPlaying) {
			if(random.Next(100) < IdleStartProb) {
				AT.Active = true;
				IdleAnimIsPlaying = true;
			}
		}
		//Check for movement
		if(!inDialogue && IdleAnimIsPlaying && CanWander) {
			if(cooldown > 0.0f) {
				cooldown -= delta;
			} else {
				cooldown = 0.0f;
				//Check for destination
				if(!isWandering) {
					AS.Travel("Walk");
					isWandering = true;
					wanderTime = (random.Next(100)/100.0f) * WanderingDist;
					//Set input vec
					InputVec = NewInputVec();
				} else {
					wanderTime -= delta;
					
					//Check for collision
					if(NextDir == Vector2.Zero) {
						if(IsOnWall() || IsOnFloor() || IsOnCeiling()) {
							KinematicCollision2D col = GetLastSlideCollision();
							NextDir = (Position - col.Position).Normalized(); 
						}
					}
					
					//Check if destination was reached
					if(wanderTime <= 0.0f) {
						StopWandering();
					}
				}
				
			}
		}
		HandleMovement(delta);
		
		if(Velocity == Vector2.Zero) {
			//Goto idle
			AS.Travel("Idle");
		} else {
			//Scale velocity and move
			Velocity = MoveAndSlide(Velocity);
			AS.Travel("Walk");
		}
	}
	
	/**
	 * @brief Handles what happens when the player enters the ListenBox area,
	 * meaning that the player has entered the zone where they should be able to 
	 * hear the NPC's dialogue. This also causes the NPC to subscribe to the player
	 * allowing for onDemand dialogue to take place.
	 * @param tb, the TalkBox of the player that has entered the zone.
	 */
	private void _on_ListenBox_area_entered(Area2D tb) {
		//Check if player is around
		if(tb.Owner is Player) {
			Player p = (Player)tb.Owner;
			//Subscribe to the player
			p._Subscribe(this);
		}
	}
	
	public bool _RequestAutoDialogue() {
		//Show auto dialogue if the NPC has one
		if(HasAutoDialogue && !inDialogue && !inAutoDialogue) {
			//Fetch the right dialogue
			string next = DC._StartDialogue(AutoDialogueID, true);
			
			//Show it in the box
			if(next != null) {
				TB._ShowText(next);
			}
			
			//Set state
			inAutoDialogue = true;
		}
		return inAutoDialogue;
	}
	
	public bool _EndAutoDialogue() {
		//Hide auto dialogue if the NPC is in one
		if(HasAutoDialogue && inAutoDialogue) {
			DC._EndDialogue();
			inAutoDialogue = false;
			//Hide the text box
			TB._HideText();
		}
		return inAutoDialogue;
	}
	
	/**
	 * @brief Handles what happens the the player is no longer in range to hear dialogue.
	 * This causes the NPC to unsubscribe to the player, making it no longer
	 * possible to generate onDemand dialogue.
	 * @param tb, the TalkBox of the player who has left the zone.
	 */
	private void _on_ListenBox_area_exited(Area2D tb) {
		if(tb.Owner is Player) {
			Player p = (Player)tb.Owner;
			//Unsubscribe to the player
			p._Unsubscribe(this);
			
			//End dialogue for DialogueController when needed
			if(HasAutoDialogue && inAutoDialogue) {
				DC._EndDialogue();
				inAutoDialogue = false;
			}
			
			//Hide the text box
			TB._HideText();
		}
	}
	
	// Formats the given DC._QueryDialogue result using the given arguments
	private string[] FormatText(string[] text, int nArgs = 0, string[] args = null) {
		//Sanity check
		if(text == null) {
			throw new Exception("Can't format null");
		}

		// Compile the array into a single string
		string oneLine = string.Join("\n", text);

		// Reformat the text
		string tFormated = nArgs > 0 ? string.Format(oneLine, args) : oneLine;

		// Return a line separated version
		return tFormated.Split('\n');
	}
	
	private string[] FormatString(string text) {
		//Sanity check
		if(text == null) {
			throw new Exception("Can't format null");
		}
		
		string newText = "";
		int count = MAX_CHAR_PER_LINE;
		int lines = MAX_LINES;
		List<string> textLines = new List<string>();
		foreach(char c in text) {
			// Ignore new lines
			if(c == '\n') continue;
			
			// Only 3 lines per entry
			if(lines == 0 || c == '¢') {
				textLines.Add(newText);
				newText = "";
				lines = MAX_LINES;
				continue;
			}
			
			// Max 25 characters per line
			if(count-- == 0) {
				count = MAX_CHAR_PER_LINE;
				lines--;
			} 
			newText += c;
		}
		if(textLines.Count > 1) {
			textLines.Add(newText);
		}
		return textLines.ToArray();
	}
	
	private void TurnToPlayer(Player player) {
		if(CanTurn) {
			InputVec = (player.Position - Position).Normalized();
			HandleMovement(0.03f);
			InputVec = Vector2.Zero;
			HandleMovement(0.03f);
		}
	}
	
	private void BeginDialogue(Player player, ref string d) {
		if(isTrueschel && context._CheckBrewBurn() != -1.0f) {
			if(context._CheckBrewBurn() < BrewBadThreshold) {
				DemandDialogueID = "demandAngeliqueBad";
			} else {
				DemandDialogueID = "demandAngeliqueGood";
			}
		}

		//Check for tutorial NPC
		if(context._GetQuest() == Quests.TUTORIAL) {
			if(context._GetQuestStateId() >= QuestController.CONFIRM_OPEN_NOTEBOOK_OBJECTIVE) {
				DemandDialogueID = "demandTuto";
			} else {
				DemandDialogueID = "preTuto";
			}
		}

		inDialogue = true;
		player._StartDialogue();
		if(isBrewer) {
			InnerLines = BrewQuestText();
			InnerLinesCount = InnerLines.Length;
			d = InnerLines[0];
		} else {
			d = DC._StartDialogue(DemandDialogueID);
		}
		
		//Turn to player
		TurnToPlayer(player);
	}
	
	private void FinishDialogue(Player player) {
		inDialogue = false;
		TB._HideText();
		player._EndDialogue();
		DC._EndDialogue();
		
		//Start the brewing minigame
		if(isBrewer) {
			if(context._CheckBrewBurn() == -1.0f) {
				context._UpdateBrewerPreviousPos(Position);
				SceneChanger SC = GetNode<SceneChanger>("/root/SceneChanger");
				SC.GotoScene("res://scenes/Brasserie/BrewGame.tscn");
			} else {
				isBrewer = false;
				isQuestNPC = false;
				context._EndBrewGameCutscene();
			}
		}
		
		if(!CanWander) {
			LookInInitalDir();
		}
	}

	
	public void _NotifyQuest(Player player) {
		string d;
		TB._HideAll();

		//Display text if needed
		if(InnerLinesCount != 0) {
			d = InnerLines[InnerLines.Length - InnerLinesCount--];
		} else {
			if(!inDialogue) {
				//Initiate the dialogue
				inDialogue = true;
				player._StartDialogue();
				TurnToPlayer(player);
				
				//Start the quest if needed
				if(context._GetLocation() == Locations.INTRO) {
					QC._StartQuest(Quests.TUTORIAL);
				}
			} 
			//Check quest status and retrieve dialogue
			d = QC._QuestInteraction();

			//Check if it's the end of the dialogue
			if(d == null) {
				//Update objective if spoken to for the first time
				if(context._GetQuest() == Quests.TUTORIAL) {
					//Check for progress
					int id = context._GetQuestStateId();
					//First stage of the tutorial is done
					context._UpdateQuestStateId((id < QuestController.TALK_TO_QUEST_NPC_OBJECTIVE) ?
						QuestController.TALK_TO_QUEST_NPC_OBJECTIVE : 
						id == QuestController.OPEN_NOTEBOOK_OBJECTIVE ? 
							QuestController.CONFIRM_OPEN_NOTEBOOK_OBJECTIVE : 
							id
					);
				}
				FinishDialogue(player);
				return;
			}
		}
		//Show it in the box
		if(d != null) {
			//throw new Exception(d);
			TB._ShowText(d);
			TB._ShowPressE();
		}
	}
	
	/**
	 * @brief Called by the player when the NPC should be notified of an interaction.
	 */
	public void _Notify(Player player) {
		TB._HideAll();
		if(HasDemandDialogue) {
			string d = null;
			
			if(InnerLinesCount != 0) {
				d = InnerLines[InnerLines.Length - InnerLinesCount--];
			} else {
				//Check if this is the start of a dialogue
				if(!inDialogue) {
					BeginDialogue(player, ref d);
					
					if(d == null) {
						throw new Exception("No starting dialogue given");
					}
				} else {
					d = DC._NextDialogue();
					
					//Check if it's the end of the dialogue
					if(d == null) {
						FinishDialogue(player);
						return;
					}
				}
				
				if(!isBrewer) {
					//Format the text to fit in the dialogue boxes
					InnerLines = FormatString(d);
					InnerLinesCount = InnerLines.Length;
				}
				
				//Update the dialogue
				if(InnerLinesCount != 0) {
					d = InnerLines[InnerLines.Length - InnerLinesCount--];
				}
			}
			
			//Show it in the box
			if(d != null) {
				TB._ShowText((string)d);
				TB._ShowPressE();
			}
		}
	}
	
	public InfoValue_t _CompareSolutions(CharacterInfo_t characterInfo, int tabId) {
		CharacterInfo_t solution = new CharacterInfo_t(-1);
		InfoValue_t[] sols = new InfoValue_t[context.NLanguages];

		// An answer in any language is valid
		var langs = Enum.GetValues(typeof(Language));
		int idx = 0;

		// Gather all possible language answers
		foreach(var l in langs) {
			solution = QuestController._QueryQuestSolution(tabId, (Language)l);
			sols[idx++] = QC._CompareCharInfo(solution, characterInfo);
		}

		// The result is a conjunction of all possible language answers
		InfoValue_t res = new InfoValue_t(false);
		for(int i = 0; i < context.NLanguages; i++) {
			res = res.Or(sols[i]);
		} 
		return res;
	}
	
	public InfoValue_t _EvaluateQuest(Player player, CharacterInfo_t characterInfo, int tabId) {
		InfoValue_t res = _CompareSolutions(characterInfo, tabId);
		
		if(!inDialogue) {
			inDialogue = true;
			player._StartDialogue();
			QC._InitBuffer(QuestText(res));
			TB._ShowText(QC._NextLine());
			TB._ShowPressE();
		} else {
			string l = QC._NextLine();
			//Check that the dialogue isn't over
			if(l == null) {
				TB._HideText();
				player._EndDialogue();
				inDialogue = false;

				//Check for game end
				if(context._AllTabsCorrect()) {
					SceneChanger SC = GetNode<SceneChanger>("/root/SceneChanger");
					SC.GotoScene("res://scenes/Interaction/EndScreen.tscn");
				}
				
			} else {
				TB._ShowText(l);
				TB._ShowPressE();
			}
		}
		return res;
	}
}

