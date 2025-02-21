using Godot;
using System;

public class TextSprite : Sprite {
	[Export]
	public string resourceName; //includine the extension, e.g. /normal.png
	[Export]
	public string resourcePath; //excluding the language and the filename, e.g. "06_UI_menus/"
	
	private const string resourceBase = "res://assets/";
	
	Context context;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		// Call the base class's ready
		base._Ready();
		
		// Get the context
		context = GetNode<Context>("/root/Context");
		
		// Update the ressource with the correct language
		UpdateResource(context._GetLanguage());
		
		// Connect the language update signal to the class
		context.Connect("UpdateLanguage", this, nameof(UpdateResource));
	}
	
	private void UpdateResource(Language l) {
		// Update the sprite
		string path = string.Format("{0}/{1}/{2}/", resourceBase, resourcePath, Context._GetLanguageAbbrv(l));
		
		// Load in both new textures
		this.Texture = (Texture) ResourceLoader.Load(path + resourceName);
	}
}
