import * as THREE from "three";

export type SelectionState = "none" | "hover" | "select" | "selecthover" | "grab";

export const neutralColor = new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue("color"));
export const backgroundColor = new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue("background"));