tool
extends EditorPlugin

func _enter_tree():
	add_custom_type("MultiagentSystem", "Spatial", preload("res://addons/multiagent_system/MultiagentSystem.cs"), preload("res://addons/multiagent_system/multiagent_system.svg"))

func _exit_tree():
	remove_custom_type("MultiagentSystem")
