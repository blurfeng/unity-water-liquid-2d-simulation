# Changelog

## [0.9.2] - 2025-10-22
### Fixed
- Fix the issue where the liquid2DLayerMask could not be modified when loading the plugin package via UPM. Remove liquid2DLayerMask and unify the identification of the Renderer Feature, fluid particles, and Volume using nameTag.

## [0.9.1] - 2025-10-21
### Fixed
- Fix the issue where the GUIDs in the .meta files of IRandomData and Random are identical to those in the .meta files of the Random utility scripts under Fs.Utility, causing a conflict.

## [0.9.0] - 2025-10-13
### Changed
- Fluid particle color mixing.
- Added occluder rendering feature, allowing you to set layers that occlude the fluid.
- Added demo sample "WaterMix" for color mixing. "Milk" for occluder rendering and pixel effects.

## [0.8.0] - 2025-09-24
- Initial release.