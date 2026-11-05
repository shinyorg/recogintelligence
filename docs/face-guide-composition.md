# Compose the face guide with your own instructions

`FaceEnrollmentView` can show its face oval independently of its instruction banner:

```csharp
enrollment.ShowGuide = true;
enrollment.ShowInstructionBanner = false;
enrollment.GuideUnalignedColor = Colors.Gold;
enrollment.GuideAlignedColor = Colors.Green;
enrollment.GuideScrimColor = Color.FromRgba(0, 0, 0, 0.35f);
enrollment.GuideTransitionDuration = TimeSpan.FromMilliseconds(250);
enrollment.StepCountdown = TimeSpan.FromSeconds(3);
enrollment.CountdownChanged += (_, tick) => ShowCountdown(tick.SecondsRemaining);
enrollment.GuideFeedbackChanged += (_, feedback) => ShowAlignment(feedback.IsAligned, feedback.Hint);
enrollment.Rejected += (_, rejection) => ShowQualityHint(rejection.Reason);
```

The countdown gates capture inside the library. At zero, enrollment waits until the face meets its guide, stability, image quality and novelty checks. `CaptureAccepted` fires only after the accepted image has been stored. The caller may place its own labels, countdown and flash above the control.

`FaceRecognitionView` supports the same oval without adding an instruction banner:

```csharp
recognition.Guide = new FaceGuide(Height: 0.73f);
recognition.ShowGuide = true;
recognition.GuideTransitionDuration = TimeSpan.FromMilliseconds(250);
recognition.GuideFeedbackChanged += (_, feedback) => ShowAlignment(feedback.IsAligned, feedback.Hint);
```

The recognition guide is visual guidance. It does not narrow detection or reject a recognized face outside the oval. `IsInsideGuide(observation)` uses the same AspectFill geometry as the drawn guide when the caller needs that fact for evidence.
