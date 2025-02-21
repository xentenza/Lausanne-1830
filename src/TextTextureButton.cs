using Godot;
using System;

public class TextTextureButton : TextureButton {
	
	[Export]
	public string resourceNameHover; //including the extension
	[Export]
	public string resourceNameNormal; //includine the extension, e.g. normal.png
	[Export]
	public string resourcePath; //excluding the language and the filename, e.g. "06_UI_menus"
	
	private const string resourceBase = "res://assets/";
	
	Context context;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		// Call the base class's ready
		base._Ready();
		
		// Get the context
		context = GetNode<Context>("/root/Context");
		
		// Update the ressource with the correct language
		UpdateRessource(context._GetLanguage());
		
		// Connect the language update signal to the class
		context.Connect("UpdateLanguage", this, nameof(UpdateRessource));
	}
	
	private void UpdateRessource(Language l) {
		// Update the sprite
		string path = string.Format("{0}/{1}/{2}/", resourceBase, resourcePath, Context._GetLanguageAbbrv(l));
		
		// Load in both new textures
		TextureHover = (Texture) ResourceLoader.Load(path +resourceNameHover);
		TextureNormal = (Texture) ResourceLoader.Load(path +resourceNameNormal);
	}
}
