# UI follow-up after private beta 2

- Restored the existing custom main caption controls while retaining native resizing. Caption input regions refresh after layout.
- Settings use a fixed-size borderless window, in-form close button, drag region, matching mint/violet lighting, and opaque backing canvas.
- Viewer client canvas is opaque underneath its content; native corner rounding controls the outer edge. The preview height follows source aspect ratio instead of a fixed 210-pixel box.
- Bundled Nunito with its OFL notice; icon fonts stay separate. Quality choices have a 14-pixel content gap and a custom left-aligned radio layout. Tooltips and confirmation surfaces follow the app palette.
- Screen saver, energy saver and Remote Desktop controls are inline in Performance. Automatic previews are under Playback. All follow draft/save persistence.
- Explore filters open as a separate attached window on the left, without a hide-main toggle. Switching to Library or closing the main window hides it.
- Confirmed crash report: SemaphoreFullException in MainViewModel.set_IsLoading. Removed the accidental release from loading state; refresh owns and releases its own gate in finally. The LibraryRefreshCheck harness exercises the real view model without a native window or user data.

Validation: Release build, settings persistence regression tests, and direct view-model rescan regression check. Native Computer Use is unavailable in this session; visual appearance and pointer interactions require fresh screenshots/manual confirmation. The beta 2 installer is unchanged by this follow-up; the local ux-fix build and main branch include the patch.
