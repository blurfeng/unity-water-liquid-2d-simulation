# Changelog

## [0.9.3] - 2025-11-03
### Changed
- Prompt when modifying the liquid particle NameTag. Updated the comparison rules for NameTag.
- Updated the liquid spawner : added options to adjust the flow rate factor and jet force factor. The diagram size in the Scene view is now fixed relative to the camera zoom.
- Added a new scene Minisize to demonstrate how to adjust liquid particle-related prefabs and parameters when the camera size is smaller.
- Updated the example images in the README.
- Other updates and adjustments, with improved code comments.

## [0.9.2] - 2025-10-22
### Fixed
- Fix the issue where the liquid2DLayerMask could not be modified when loading the plugin package via UPM. Remove liquid2DLayerMask and unify the identification of the Renderer Feature, liquid particles, and Volume using nameTag.

## [0.9.1] - 2025-10-21
### Fixed
- Fix the issue where the GUIDs in the .meta files of IRandomData and Random are identical to those in the .meta files of the Random utility scripts under Fs.Utility, causing a conflict.

## [0.9.0] - 2025-10-13
### Changed
- Liquid particle color mixing.
- Added occluder rendering feature, allowing you to set layers that occlude the liquid.
- Added demo sample "WaterMix" for color mixing. "Milk" for occluder rendering and pixel effects.

## [0.8.0] - 2025-09-24
- Initial release.