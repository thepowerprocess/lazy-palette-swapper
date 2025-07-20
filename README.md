# Unity Lazy Palette Swapper

## Fork Additions:
* Preview live changes of output texture
  * <img width="582" height="307" alt="image" src="https://github.com/user-attachments/assets/8f59ca52-624d-4d4c-9e76-73e6e55919f2" />\
  * Clicking either texture pings it in your project.
* Auto save texture.  Allowing to see changes in the scene live.
  * <img width="317" height="120" alt="image" src="https://github.com/user-attachments/assets/3657ecf6-03ef-4e23-b38c-4b0c050d0e4f" />
* Palette color limit to prevent crashing the tool.
  * <img width="261" height="56" alt="image" src="https://github.com/user-attachments/assets/d8bde479-56d4-408b-9ccd-52c8621175c1" />
* New texture copies the source texture import settings (Pixels Per Unit, Filter Mode, etc).
  * <img width="403" height="233" alt="image" src="https://github.com/user-attachments/assets/75089ede-237f-4fea-9a85-5d02e9844790" />
* Creating multiple palettes that are all linked to the same source texture that can be modified easily later.
  * This is done through file names
  * <img width="565" height="113" alt="image" src="https://github.com/user-attachments/assets/2ab05efe-502a-499a-a545-62b783592b0a" />
  * When the source texture is imported into the tool, all the related palettes are populated (as long as they are in the same folder).  Allowing you to switch between palettes easily to modify and copy them to start new palettes.
    * <img width="276" height="119" alt="image" src="https://github.com/user-attachments/assets/3f3de499-ef39-4b88-8c12-4a2efda7beed" />
* Buttons to reset the palette color back to the original source color (on the right side) and a button to reset all the colors back to the source.
  * <img width="560" height="224" alt="image" src="https://github.com/user-attachments/assets/586a33ea-ac3f-4e8f-a03a-d23174dba69b" />
*  Hue - Saturation - Value Tool for making a palette that shifts the source colors.  Then can individually modify colors like normal after using the tool.
  * <img width="568" height="742" alt="image" src="https://github.com/user-attachments/assets/312cfebf-c789-4cc3-8e68-5d8112ec690e" />
* Tool settings persist.  When opening the tool window it will auto load the last source texture.
* Issues
  * It seems to mostly work.  However, when auto saving is enabled, I tried to prevent it from lagging like crazy when changing the colors, but sometimes it does throw some warnings about file modication data.  Couldn't figure out a way to avoid it. 

--------------------

The Lazy Palette Swapper is tool that allows users to easily swap the Colour Palettes of their Pixel Artwork<br>
(individual sprites or spritesheets) within the Unity Editor.

In the current project I have included a few demo Spritesheet that you can use to test the tool with. They vary in size so that you
can benchmark the tool on your machine accordingly.

**WARNING**<br>
This tool was design for Pixel Art, and not for extremely detailed Images. You can try, but you have been warned!

## How to use

### 1. Open The Lazy Palette Swapper

![](~Documentation/Images/open-tool.png)

You can find the Tool by navigating the MenuBar - Lazy-Jedi/Tools/Lazy Palette Swapper.

### 2. Use Lazy Palette Swapper

![](~Documentation/Images/palette-swapper-tool-default.png)

When you open the tool for the first time it will look like the image above. In the following sections I will explain how to use the tool correctly.

#### Source Texture

![](~Documentation/Images/source-texture-settings.png)

* Source Texture - The Source Texture is the texture you wish to change with a new Colour Palette.

The original Color Palette will also be extracted from this image and can be used as a guide when mapping your new Colours.

#### Advanced Settings

![](~Documentation/Images/advanced-settings.png)

* Use Async - Processes the "Get Palette" and "Swap Palette" on separate threads to prevent the Unity Editor from hanging.


* Ignore Colours with Alpha - Adjust this value to Ignore any Pixels that has a Less Than or Equal to Alpha Value

#### Palette Settings

![](~Documentation/Images/palette-settings.png)

* Source Texture Palette - This is a static colour palette of the Source Texture, you can use to to identify colours you wish to change.


* Editable Palette - This is a editable colour palette, any colour on the Left (Source Texture Palette) will be replaced with
  the colour you changed in this editable colours list. Make sure to reference the Source Texture Palette to identify any colours you wish to swap.

#### Output Settings

![](~Documentation/Images/output-settigns.png)

* Output Path - This will automatically be completed for you, however, if you wish to save the image with a new name or save it
  to a new location you can either edit the output path directly or use the Browse Button to navigate to a directory where you wish to save the newly created
  image.


* Get Palette Button - This button will show if you have not generated the Palette for the Source Image. Once Clicked, it will
  retrieve a unique list of Colours from the Source Texture.


* Swap Palette Button - This button will show if you have a valid Palette that can be used to produce a new image by swapping
  out the original palette for your new palette.

## Future Plans
* Implement a much faster Algorithm to Get and Swap Palettes
* Save Palette as a Palette Strip
* Added option to use a Palette png to change colours of Source Texture

## Credits

Vryell - [Tiny Adventure Pack](https://vryell.itch.io/tiny-adventure-pack)
