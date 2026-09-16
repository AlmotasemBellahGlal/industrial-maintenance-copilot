// English source keys provide deterministic fallback. Never translate source evidence or identifiers.
export const arabic: Readonly<Record<string, string>> = {
  'Grounded fallback — no work order created': 'إجابة بديلة مستندة إلى الأدلة — لم يُنشأ أمر عمل',
  'This advisory answer grants no safety approval or dispatch permission.':
    'هذه إجابة استشارية لا تمنح اعتماد سلامة أو إذن إرسال.',
  'Grounded fallback started': 'بدأت الإجابة البديلة المستندة إلى الأدلة',
  'Grounded fallback completed': 'اكتملت الإجابة البديلة المستندة إلى الأدلة',
  'Retry scheduled: attempt {0}': 'إعادة المحاولة مجدولة: المحاولة {0}',
  transient_exhausted: 'استُنفدت محاولات معالجة العطل المؤقت',
  agent_timeout: 'انتهت مهلة الوكيل',
  provider_timeout: 'انتهت مهلة المزوّد',
  Degraded: 'إجابة بديلة استشارية',
  DegradedRefused: 'الأدلة لا تكفي لإجابة بديلة',

  'Proposal — review required': 'مقترح — تلزم المراجعة',
  TimedOut: 'انتهت المهلة',
  Conflict: 'تعارض',
  NotDispatchable: 'غير مؤهل للإرسال',
  'Skip to main content': 'انتقل إلى المحتوى الرئيسي',
  'Maintenance Copilot home': 'الصفحة الرئيسية لمساعد الصيانة',
  MAINTENANCE: 'الصيانة',
  COPILOT: 'المساعد',
  Primary: 'التنقل الرئيسي',
  OPERATIONS: 'العمليات',
  'HOST ACCESS': 'الوصول إلى المضيف',
  Connection: 'الاتصال',
  'Human authority. Always.': 'القرار البشري أولًا، دائمًا.',
  'AI proposes. Supervisors approve. Trusted safety checks govern dispatch.':
    'يقترح الذكاء الاصطناعي ويوافق المشرفون. وتتحكم فحوص السلامة الموثوقة في الإرسال.',
  'INDUSTRIAL OPERATIONS': 'العمليات الصناعية',
  '/ WORKSPACE': '/ مساحة العمل',
  'Evidence informs decisions. The trusted host enforces safety and permissions.':
    'تدعم الأدلة القرارات. ويفرض المضيف الموثوق ضوابط السلامة والصلاحيات.',
  'Supervisor workspace': 'مساحة عمل المشرف',
  'Work order review': 'مراجعة أمر العمل',
  'Review the executable scope. Keep approval, safety verification and dispatch separate.':
    'راجع نطاق التنفيذ. الموافقة والتحقق من السلامة والإرسال خطوات منفصلة.',
  'Reload current review': 'إعادة تحميل المراجعة الحالية',
  'Connection settings': 'إعدادات الاتصال',
  'Actions are paused. Reload and review the current server state before continuing. Edited fields remain visible until reload.':
    'الإجراءات متوقفة مؤقتًا. أعد التحميل وراجع حالة الخادم قبل المتابعة. تبقى التعديلات ظاهرة حتى إعادة التحميل.',
  'Executable scope': 'نطاق التنفيذ',
  'AI-origin proposal content is advisory until reviewed. Evidence does not grant safety or dispatch authority.':
    'مقترح الذكاء الاصطناعي استشاري حتى مراجعته. الأدلة لا تمنح اعتماد السلامة أو صلاحية الإرسال.',
  Equipment: 'المعدة',
  'Reported symptom': 'العَرَض المُبلغ عنه',
  Manual: 'الدليل',
  'Manual revision': 'إصدار الدليل',
  'Ordered actions': 'الإجراءات المرتبة',
  'Submit for supervisor review': 'إرسال لمراجعة المشرف',
  'Edit & approve': 'تعديل وموافقة',
  Reject: 'رفض',
  'Trusted host assessment': 'تقييم المضيف الموثوق',
  'Authoritative safety requirements': 'متطلبات السلامة المعتمدة',
  'Assessment revision:': 'إصدار التقييم:',
  '. Human verification is separate from supervisor approval.':
    '. التحقق البشري منفصل عن موافقة المشرف.',
  'Record a human safety check': 'تسجيل فحص سلامة بشري',
  'Only an authorized verifier may record checks actually performed. AI cannot verify these conditions.':
    'لا يسجل الفحوص المنفذة فعليًا إلا مُتحقق مُخوّل. لا يستطيع الذكاء الاصطناعي التحقق من هذه الشروط.',
  Prerequisite: 'المتطلب المسبق',
  'Select prerequisite': 'اختر المتطلب المسبق',
  'Physical verification evidence': 'دليل التحقق الميداني',
  'I verified this prerequisite is satisfied': 'تحققت من استيفاء هذا المتطلب المسبق',
  'Unchecked records an unsatisfied prerequisite; it does not satisfy the dispatch gate.':
    'عدم التأشير يسجل المتطلب غير مستوفى؛ ولا يحقق شرط الإرسال.',
  'Select a prerequisite and provide nonblank evidence (up to 4,000 characters).':
    'اختر متطلبًا وأدخل دليلًا غير فارغ (حتى ٤٬٠٠٠ حرف).',
  'Review verification': 'مراجعة التحقق',
  'Final scope review': 'مراجعة النطاق النهائي',
  'Equipment is fixed. Changing the manual, revision or instructions requires a new trusted assessment. Requirements below cannot be edited by the browser.':
    'المعدة ثابتة. تغيير الدليل أو الإصدار أو التعليمات يتطلب تقييمًا موثوقًا جديدًا. لا يمكن تعديل المتطلبات أدناه من المتصفح.',
  'Manual ID': 'معرّف الدليل',
  'Enter a nonempty manual UUID.': 'أدخل معرّف UUID صالحًا وغير فارغ للدليل.',
  'Manual revision ID': 'معرّف إصدار الدليل',
  'Enter a nonempty manual revision UUID.': 'أدخل معرّف UUID صالحًا وغير فارغ لإصدار الدليل.',
  'Enter a symptom of 1–2,000 characters.': 'أدخل وصف العَرَض من حرف واحد إلى ٢٬٠٠٠ حرف.',
  'Work order description': 'وصف أمر العمل',
  'Enter a description of 1–4,000 characters.': 'أدخل وصفًا من حرف واحد إلى ٤٬٠٠٠ حرف.',
  Remove: 'إزالة',
  'Enter an instruction of 1–2,000 characters.': 'أدخل تعليمات من حرف واحد إلى ٢٬٠٠٠ حرف.',
  'Add action': 'إضافة إجراء',
  'Use valid manual UUIDs and nonblank text. Symptom/actions: up to 2,000 characters; description: up to 4,000. At least one action is required.':
    'استخدم معرّفات UUID صالحة ونصوصًا غير فارغة. الحد الأقصى للعَرَض والإجراءات ٢٬٠٠٠ حرف وللوصف ٤٬٠٠٠ حرف. يلزم إجراء واحد على الأقل.',
  'Assess edited scope': 'تقييم النطاق المعدّل',
  'Cancel editing': 'إلغاء التعديل',
  'Authoritative requirements for this exact edit': 'المتطلبات المعتمدة لهذا التعديل الدقيق',
  'Trusted policy explicitly assessed an empty requirement set.':
    'قيّمت السياسة الموثوقة صراحةً مجموعة متطلبات فارغة.',
  'Confirming approves this final edited scope and these requirements. Old verifications do not transfer. Any further edit clears this preview.':
    'التأكيد يعتمد النطاق النهائي المعدّل وهذه المتطلبات. لا تنتقل التحققات السابقة. أي تعديل لاحق يمسح هذه المعاينة.',
  'Review final edit & approval': 'مراجعة التعديل النهائي والموافقة',
  'Manual provenance': 'مصدر الدليل',
  'Grounded evidence': 'الأدلة المستندة إلى المصدر',
  'Original proposal citations': 'استشهادات المقترح الأصلي',
  "Historical citations from the original proposal. They are not proof that a later edited scope is supported. The server's exact-scope policy remains authoritative.":
    'استشهادات تاريخية من المقترح الأصلي. لا تثبت دعم نطاق معدّل لاحقًا. تبقى سياسة الخادم للنطاق الدقيق هي المرجع المعتمد.',
  Document: 'المستند',
  Revision: 'الإصدار',
  Chunk: 'المقطع',
  'No citations returned. This view does not imply grounding where none is available.':
    'لم تُرجع استشهادات. لا تعني هذه الشاشة وجود أدلة غير متاحة.',
  'External side effect': 'إجراء ذو أثر خارجي',
  'Dispatch confirmation': 'تأكيد الإرسال',
  'The server must confirm current supervisor approval and all mandatory verified prerequisites. Browser state alone never proves eligibility.':
    'يجب أن يتحقق الخادم من موافقة المشرف الحالية واستيفاء جميع المتطلبات الإلزامية المتحقق منها. حالة المتصفح وحدها لا تثبت الأهلية.',
  'Review dispatch request': 'مراجعة طلب الإرسال',
  'A dispatch request was made in this view. Do not repeat it. Inspect the existing attempt or current work order; uncertain delivery is neither success nor failure.':
    'قُدم طلب إرسال في هذه الشاشة. لا تكرره. افحص المحاولة القائمة أو أمر العمل الحالي؛ النتيجة غير المؤكدة ليست نجاحًا ولا فشلًا.',
  'Host access': 'الوصول إلى المضيف',
  'Connect your workspace': 'ربط مساحة العمل',
  'Use a credential issued by your trusted maintenance host.':
    'استخدم بيانات اعتماد صادرة عن مضيف الصيانة الموثوق.',
  'Session credential': 'بيانات اعتماد الجلسة',
  'Credentials stay in memory until you clear them or reload. Your actor identity and equipment permissions are determined by the server.':
    'تبقى بيانات الاعتماد في الذاكرة حتى مسحها أو إعادة التحميل. يحدد الخادم هويتك وصلاحياتك للمعدات.',
  'Host bearer credential': 'رمز اعتماد المضيف',
  'Paste the credential configured by your administrator. Never enter an actor ID as a substitute.':
    'ألصق بيانات الاعتماد التي أعدها المسؤول. لا تستبدلها بمعرّف مستخدم.',
  'Enter a credential between 32 and 512 characters.':
    'أدخل بيانات اعتماد بطول من ٣٢ إلى ٥١٢ حرفًا.',
  'Use credential': 'استخدام بيانات الاعتماد',
  'Clear session': 'مسح الجلسة',
  'API readiness': 'جاهزية واجهة البرمجة',
  'Checks the configured database schemas. It does not validate your credential or prove an LLM provider is available.':
    'يفحص مخططات قواعد البيانات المضبوطة. لا يتحقق من بيانات اعتمادك ولا يثبت توفر مزود النموذج اللغوي.',
  'Operations overview': 'نظرة عامة على العمليات',
  'Maintenance, with evidence.': 'صيانة تستند إلى الأدلة.',
  'A clear path from reported symptom to a safely reviewed work order.':
    'مسار واضح من العَرَض المُبلغ عنه إلى أمر عمل تمت مراجعته بأمان.',
  'Start a diagnosis': 'بدء تشخيص',
  'The maintenance workflow': 'سير عمل الصيانة',
  'Three agents. One human decision.': 'ثلاثة وكلاء. قرار بشري واحد.',
  'Match the symptom': 'مطابقة العَرَض',
  'Use the selected equipment and applicable manual revision.':
    'استخدم المعدة المحددة وإصدار الدليل المنطبق عليها.',
  'Plan diagnostics & safety': 'تخطيط التشخيص والسلامة',
  'Ground instructions in evidence. Trusted policy determines requirements.':
    'أسند التعليمات إلى الأدلة. تحدد السياسة الموثوقة المتطلبات.',
  'Review the proposal': 'مراجعة المقترح',
  'A supervisor reviews the exact scope before safety verification and dispatch.':
    'يراجع المشرف النطاق الدقيق قبل التحقق من السلامة والإرسال.',
  'Safety boundary': 'ضوابط السلامة',
  'A proposal is not permission.': 'المقترح ليس إذنًا.',
  'Supervisor approval and verified mandatory prerequisites are separate gates. The server rechecks both when dispatch is requested.':
    'موافقة المشرف والتحقق من المتطلبات الإلزامية شرطان منفصلان. يعيد الخادم فحصهما عند طلب الإرسال.',
  'Open a work order': 'فتح أمر عمل',
  'Recent session activity': 'نشاط الجلسة الأخير',
  'Records visited in this browser session. Observations may be stale; open a record to refresh. This is not a system-wide inventory.':
    'سجلات تمت زيارتها في جلسة المتصفح. قد تكون المعلومات قديمة؛ افتح السجل لتحديثه. هذه ليست قائمة شاملة للنظام.',
  'Your workspace is ready': 'مساحة عملك جاهزة',
  'Start a diagnosis or look up a known run, work order, dispatch attempt, or trace.':
    'ابدأ تشخيصًا أو ابحث عن تشغيل أو أمر عمل أو محاولة إرسال أو تتبع بمعرّف معلوم.',
  'Look up a maintenance run': 'البحث عن تشغيل صيانة',
  'Technician workspace': 'مساحة عمل الفني',
  'New diagnosis': 'تشخيص جديد',
  'Describe the symptom. Follow the grounded workflow to a reviewable proposal.':
    'صف العَرَض. تابع سير العمل المستند إلى الأدلة للوصول إلى مقترح قابل للمراجعة.',
  'A host credential is required.': 'يلزم إدخال بيانات اعتماد المضيف.',
  'Configure connection': 'إعداد الاتصال',
  'Equipment & symptom': 'المعدة والعَرَض',
  'Equipment ID': 'معرّف المعدة',
  "Use a supported equipment UUID from your deployment's reviewed procedure configuration.":
    'استخدم معرّف UUID لمعدة مدعومة ضمن إعدادات الإجراءات المراجعة لنشرك.',
  'Enter a nonempty equipment UUID.': 'أدخل معرّف UUID صالحًا وغير فارغ للمعدة.',
  'Describe the observed condition, operating context and changes.':
    'صف الحالة الملحوظة وظروف التشغيل والتغيرات.',
  'Describe the symptom using 1–2,000 characters.': 'صف العَرَض باستخدام حرف واحد إلى ٢٬٠٠٠ حرف.',
  'Stop listening & request cancellation': 'إيقاف الاستماع وطلب الإلغاء',
  'Disconnecting requests cancellation; it does not prove durable execution stopped. Inspect any known run before starting again.':
    'قطع الاتصال يطلب الإلغاء؛ لكنه لا يثبت توقف التنفيذ المسجل. افحص التشغيل المعروف قبل البدء مجددًا.',
  'Live workflow progress': 'تقدم سير العمل المباشر',
  'Safe execution events only. No hidden reasoning or estimated percentage.':
    'أحداث تنفيذ آمنة فقط. لا تفكير داخلي خفي ولا نسبة تقديرية.',
  'Progress will appear when the host starts the workflow.':
    'يظهر التقدم عند بدء المضيف لسير العمل.',
  'Inspect run': 'فحص التشغيل',
  'Review proposed work order': 'مراجعة أمر العمل المقترح',
  'Inspect safe trace': 'فحص التتبع الآمن',
  'Record inspection': 'فحص السجل',
  'Open a known identifier. The host checks your access on every request.':
    'افتح معرّفًا معلومًا. يتحقق المضيف من صلاحية وصولك في كل طلب.',
  'Enter a nonempty UUID.': 'أدخل معرّف UUID صالحًا وغير فارغ.',
  'Open record': 'فتح السجل',
  'Loading current server record…': 'جارٍ تحميل سجل الخادم الحالي…',
  'No record loaded. There is no global inventory endpoint; use a known ID or session activity below.':
    'لم يُحمّل سجل. لا توجد نقطة وصول لقائمة شاملة؛ استخدم معرّفًا معلومًا أو نشاط الجلسة أدناه.',
  'Maintenance run': 'تشغيل الصيانة',
  Run: 'التشغيل',
  'Cancellation intent': 'طلب الإلغاء',
  'Work orders': 'أوامر العمل',
  'No work order published.': 'لم يُنشر أمر عمل.',
  'Execution traces': 'تتبعات التنفيذ',
  'No accessible trace linked.': 'لا يوجد تتبع مرتبط متاح.',
  'Refresh run': 'تحديث التشغيل',
  'External dispatch': 'الإرسال الخارجي',
  Attempt: 'المحاولة',
  'External reference': 'المرجع الخارجي',
  'Gate outcome': 'نتيجة فحص الشروط',
  'Refresh dispatch status': 'تحديث حالة الإرسال',
  'Inspect work order': 'فحص أمر العمل',
  'Refresh only queries the existing attempt. It never sends another dispatch.':
    'التحديث يستعلم عن المحاولة الحالية فقط. لا ينفذ إرسالًا آخر.',
  'Execution activity': 'نشاط التنفيذ',
  Execution: 'التنفيذ',
  Correlation: 'معرّف الارتباط',
  'Safe stage metadata from the host. Token/cost details and hidden reasoning are not exposed by this endpoint.':
    'بيانات مراحل آمنة من المضيف. لا تعرض نقطة الوصول تفاصيل الرموز أو التكلفة أو التفكير الداخلي.',
  'No recorded steps.': 'لا توجد خطوات مسجلة.',
  'Known in this session': 'المعروف في هذه الجلسة',
  'Visit records or start a diagnosis to populate this list. These are observations, not live server totals.':
    'زر السجلات أو ابدأ تشخيصًا لملء هذه القائمة. هذه ملاحظات وليست إجماليات مباشرة من الخادم.',
  'Consequential action': 'إجراء ذو آثار فعلية',
  'The server validates the current revision and your permission. This confirmation does not bypass safety checks.':
    'يتحقق الخادم من الإصدار الحالي وصلاحياتك. لا يتجاوز هذا التأكيد فحوص السلامة.',
  'Go back': 'رجوع',
  Dashboard: 'لوحة المعلومات',
  'Maintenance runs': 'تشغيلات الصيانة',
  Dispatch: 'الإرسال',
  'Trace / activity': 'التتبع / النشاط',
  'Open navigation': 'فتح قائمة التنقل',
  'Close navigation': 'إغلاق قائمة التنقل',
  'Credential configured': 'بيانات الاعتماد مضبوطة',
  'Connect to host': 'الاتصال بالمضيف',
  'Checking…': 'جارٍ الفحص…',
  'Check readiness': 'فحص الجاهزية',
  'Not checked': 'لم يُفحص',
  'Workflow running…': 'سير العمل قيد التنفيذ…',
  'Start grounded diagnosis': 'بدء التشخيص المستند إلى الأدلة',
  'Waiting for confirmation or host response…': 'بانتظار التأكيد أو استجابة المضيف…',
  'Mandatory prerequisite': 'متطلب إلزامي',
  'Optional prerequisite': 'متطلب اختياري',
  Mandatory: 'إلزامي',
  Optional: 'اختياري',
  'Not assessed': 'لم يُقيّم',
  'Not confirmed': 'غير مؤكد',
  'Not requested': 'لم يُطلب',
  'Requested — inspect lifecycle status for acknowledgement':
    'طُلب الإلغاء — افحص حالة سير العمل لمعرفة تأكيده',
  'Explicit server assessment contains no mandatory requirement set.':
    'تقييم الخادم الصريح لا يتضمن متطلبات إلزامية.',
  'No current safety assessment. An empty list does not authorize dispatch.':
    'لا يوجد تقييم سلامة حالي. القائمة الفارغة لا تجيز الإرسال.',
  Ready: 'جاهز',
  Running: 'قيد التنفيذ',
  Queued: 'في الانتظار',
  WaitingForApproval: 'بانتظار الموافقة',
  PendingApproval: 'بانتظار موافقة المشرف',
  Approved: 'مُعتمد',
  Rejected: 'مرفوض',
  Dispatched: 'تم الإرسال',
  Cancelled: 'مُلغى',
  Failed: 'فشل',
  Blocked: 'متوقف لعدم استيفاء الشروط',
  Completed: 'مكتمل',
  Uncertain: 'غير مؤكد',
  Pending: 'قيد الانتظار',
  Confirmed: 'مؤكد',
  DefinitivelyFailed: 'فشل مؤكد',
  Unverified: 'لم يُتحقق منه',
  'Not verified': 'لم يُتحقق منه',
  Satisfied: 'مستوفى',
  Unsatisfied: 'غير مستوفى',
  InsufficientEvidence: 'أدلة غير كافية',
  CannotProceed: 'يتعذر المتابعة',
  Proposed: 'تم تقديم المقترح',
  Draft: 'مسودة',
  'Supervisor approved': 'وافق المشرف',
  Inspected: 'تم الفحص',
  'AI explanation — advisory only': 'شرح الذكاء الاصطناعي — استشاري فقط',
  'Approve revision {0}?': 'الموافقة على الإصدار {0}؟',
  'Reject revision {0}?': 'رفض الإصدار {0}؟',
  'Approve final edited scope?': 'الموافقة على النطاق المعدّل النهائي؟',
  'Confirm approve': 'تأكيد الموافقة',
  'Confirm reject': 'تأكيد الرفض',
  'Confirm edit & approval': 'تأكيد التعديل والموافقة',
  'Approve the displayed edited scope and authoritative requirements as the final revision after revision {0}. Old safety verifications do not transfer.':
    'اعتماد النطاق المعدّل المعروض والمتطلبات المعتمدة كإصدار نهائي بعد الإصدار {0}. لا تنتقل تحققات السلامة السابقة.',
  'Approve work order revision {0}: {1}. Approval does not verify safety or dispatch the work order.':
    'الموافقة على إصدار أمر العمل {0}: {1}. الموافقة لا تتحقق من السلامة ولا ترسل أمر العمل.',
  'Reject work order revision {0}: {1}. No dispatch is authorized.':
    'رفض إصدار أمر العمل {0}: {1}. لا يُسمح بأي إرسال.',
  'Server decision: {0}. Review the resulting revision and safety state below.':
    'قرار الخادم: {0}. راجع الإصدار الناتج وحالة السلامة أدناه.',
  'Review submission: {0}': 'نتيجة طلب المراجعة: {0}',
  'Record human safety verification?': 'تسجيل تحقق بشري من السلامة؟',
  'Record verification': 'تسجيل التحقق',
  '{0}: record satisfied for revision {1}, with the evidence you entered. Only attest to checks actually performed.':
    '{0}: تسجيل الاستيفاء للإصدار {1} مع الدليل الذي أدخلته. لا تشهد إلا على فحوص نُفذت فعليًا.',
  '{0}: record not satisfied for revision {1}, with the evidence you entered. Only attest to checks actually performed.':
    '{0}: تسجيل عدم الاستيفاء للإصدار {1} مع الدليل الذي أدخلته. لا تشهد إلا على فحوص نُفذت فعليًا.',
  'Request external dispatch?': 'طلب إرسال خارجي؟',
  'Request dispatch': 'طلب الإرسال',
  'Request dispatch of revision {0}: {1}. This can create an external maintenance ticket. The server must recheck approval and all mandatory safety prerequisites.':
    'طلب إرسال الإصدار {0}: {1}. قد ينشئ تذكرة صيانة خارجية. يجب أن يعيد الخادم التحقق من الموافقة وجميع متطلبات السلامة الإلزامية.',
  'Workflow outcome: {0}. Inspect the durable run for details.':
    'نتيجة سير العمل: {0}. افحص التشغيل المسجل لمعرفة التفاصيل.',
  'The stream could not complete. Inspect the known run before retrying; it cannot be resumed.':
    'تعذر اكتمال البث. افحص التشغيل المعروف قبل إعادة المحاولة؛ لا يمكن استئناف البث.',
  'Connecting to the workflow…': 'جارٍ الاتصال بسير العمل…',
  'Disconnected; cancellation requested. Inspect the run to confirm its durable state.':
    'انقطع الاتصال وطُلب الإلغاء. افحص التشغيل للتأكد من حالته المسجلة.',
  'Proposal ready for human review. No dispatch has been authorized.':
    'المقترح جاهز للمراجعة البشرية. لم يُسمح بأي إرسال.',
  'Trusted safety policy accepted the proposed scope': 'قبلت سياسة السلامة الموثوقة النطاق المقترح',
  'Trusted safety policy blocked continuation': 'منعت سياسة السلامة الموثوقة المتابعة',
  'Credential configured for this session. The server will authenticate each request.':
    'ضُبطت بيانات الاعتماد لهذه الجلسة. سيتحقق الخادم من كل طلب.',
  'Credential and session activity cleared.': 'مُسحت بيانات الاعتماد ونشاط الجلسة.',
  'Trusted safety policy blocked this edited scope. No approval was recorded.':
    'منعت سياسة السلامة الموثوقة هذا النطاق المعدّل. لم تُسجّل موافقة.',
  'Decision returned no current review. Reload before continuing.':
    'لم يُرجع القرار مراجعة حالية. أعد التحميل قبل المتابعة.',
  'Verification saved by the trusted host. Current safety state refreshed.':
    'حفظ المضيف الموثوق التحقق. حُدّثت حالة السلامة الحالية.',
  'Enter a valid record UUID.': 'أدخل معرّف UUID صالحًا للسجل.',
  'External outcome is uncertain. This is neither success nor failure. The Worker reconciles the existing attempt; do not submit another delivery.':
    'النتيجة الخارجية غير مؤكدة، وليست نجاحًا ولا فشلًا. يعالج العامل المحاولة القائمة؛ لا تطلب إرسالًا آخر.',
  'Delivery remains pending. The Worker can inspect unresolved attempts using the same durable key.':
    'الإرسال لا يزال قيد الانتظار. يمكن للعامل فحص المحاولات غير المحسومة باستخدام المفتاح الدائم نفسه.',
  'External acceptance confirmed by the trusted host.': 'أكد المضيف الموثوق القبول الخارجي.',
  'External delivery definitively failed. No automatic browser retry will be made.':
    'فشل الإرسال الخارجي بشكل مؤكد. لن يعيد المتصفح المحاولة تلقائيًا.',
  'Inspect the gate outcome. Delivery has not been confirmed.':
    'افحص نتيجة الشروط. لم يتأكد الإرسال.',
  'The request is invalid. Check the required fields and identifiers.':
    'الطلب غير صالح. تحقق من الحقول المطلوبة والمعرّفات.',
  'Authentication required. Update your host credential in Connection.':
    'يلزم تسجيل الدخول. حدّث بيانات اعتماد المضيف في صفحة الاتصال.',
  'Permission denied. Your host account cannot perform this operation for this equipment.':
    'الإذن مرفوض. لا يملك حساب المضيف صلاحية هذه العملية لهذه المعدة.',
  'Record not found or unavailable to this account. Check the identifier.':
    'السجل غير موجود أو غير متاح لهذا الحساب. تحقق من المعرّف.',
  'Review changed or lifecycle conflict. Reload the current record and review again. Your request was not applied.':
    'تغيرت المراجعة أو حدث تعارض في سير العمل. أعد تحميل السجل الحالي وراجعه مجددًا. لم يُطبّق طلبك.',
  'The server did not accept this operation. Review the current lifecycle and authoritative safety requirements.':
    'لم يقبل الخادم هذه العملية. راجع حالة سير العمل ومتطلبات السلامة المعتمدة.',
  'The operation timed out. Inspect the known record before attempting another operation.':
    'انتهت مهلة العملية. افحص السجل المعروف قبل محاولة عملية أخرى.',
  'Connection or dependency unavailable. The outcome may be unknown. Inspect the record before retrying a consequential operation.':
    'الاتصال أو إحدى الخدمات غير متاح. قد تكون النتيجة غير معروفة. افحص السجل قبل إعادة محاولة عملية ذات آثار فعلية.',
  'Workflow Started': 'بدأ سير العمل',
  'Work Order Ready': 'أمر العمل جاهز',
  'Waiting For Approval': 'بانتظار الموافقة',
  'Symptom Matcher — started': 'بدأ وكيل مطابقة العَرَض',
  'Symptom Matcher — completed': 'اكتمل وكيل مطابقة العَرَض',
  'Diagnostic & Safety Planner — started': 'بدأ وكيل تخطيط التشخيص والسلامة',
  'Diagnostic & Safety Planner — completed': 'اكتمل وكيل تخطيط التشخيص والسلامة',
  'Work Order Generator — started': 'بدأ وكيل إنشاء أمر العمل',
  'Work Order Generator — completed': 'اكتمل وكيل إنشاء أمر العمل',
  runs: 'تشغيلات الصيانة',
  'work-orders': 'أوامر العمل',
  dispatch: 'الإرسال',
  traces: 'التتبعات',
  'Approve revision': 'الموافقة على الإصدار',
  'This work order is': 'حالة أمر العمل',
  '. Further editing and dispatch are unavailable.': '. التعديل والإرسال غير متاحين.',
  'Verified by': 'تحقق منه',
  Action: 'الإجراء',
  'Evidence could not be loaded:': 'تعذر تحميل الأدلة:',
  'Inspect dispatch attempt': 'فحص محاولة الإرسال',
  Observed: 'وقت الملاحظة',
  ID: 'المعرّف',
  'Code:': 'الرمز:',
  'Database schemas ready': 'مخططات قواعد البيانات جاهزة',
  rejected: 'مرفوض',
  dispatched: 'تم الإرسال',
  'Remove action': 'إزالة الإجراء',
  'Supervisor approval:': 'موافقة المشرف:',
  'Knowledge base': 'قاعدة المعرفة',
  'Ask with citations': 'اسأل مع المراجع',
  'Advisory answers from one manual revision. No approval or dispatch authority.':
    'إجابات استشارية من إصدار دليل واحد. لا تمنح اعتمادًا أو صلاحية تنفيذ.',
  Conversations: 'المحادثات',
  'History is stored on the host. Reconnect with your credential after reload.':
    'يُحفظ السجل على الخادم. أعد الاتصال ببياناتك بعد إعادة تحميل الصفحة.',
  'Refresh history': 'تحديث السجل',
  Previous: 'السابق',
  Next: 'التالي',
  'No conversations yet.': 'لا توجد محادثات بعد.',
  'New conversation': 'محادثة جديدة',
  'Document ID': 'معرف المستند',
  'Revision ID': 'معرف الإصدار',
  'Create conversation': 'إنشاء محادثة',
  Conversation: 'المحادثة',
  'Answer language': 'لغة الإجابة',
  'More history': 'المزيد من السجل',
  'Your question': 'سؤالك',
  'Ask / retry as new request': 'اسأل / أعد المحاولة بطلب جديد',
  'Cancel answer': 'إلغاء الإجابة',
  Streaming: 'جارٍ توليد الإجابة',
  'Not enough matching evidence. Refine the question or ingest the applicable manual.':
    'لا توجد أدلة مطابقة كافية. وضّح السؤال أو ارفع الدليل المناسب.',
  'Ingest manual': 'إدخال دليل',
  'Text or PDF, up to 16 MB. Uploading a manual never authorizes maintenance work.':
    'ملف نصي أو PDF بحد أقصى 16 ميجابايت. رفع الدليل لا يمنح إذنًا بأعمال الصيانة.',
  'Ingestion permission required. Connect with an authorized account.':
    'يلزم تصريح الإدخال. اتصل بحساب مخوّل.',
  Title: 'العنوان',
  'Revision number': 'رقم الإصدار',
  'Manual file': 'ملف الدليل',
  'Keep the same document and revision IDs when retrying. Re-ingestion replaces that revision without duplicates.':
    'احتفظ بنفس معرفَي المستند والإصدار عند إعادة المحاولة. يُستبدل محتوى الإصدار دون تكرار.',
  'Upload and index': 'رفع وفهرسة',
  'Refresh ingestion status': 'تحديث حالة الإدخال',
  Processing: 'جارٍ المعالجة',
  Pages: 'الصفحات',
  Chunks: 'المقاطع',
  'Document could not be processed. Check format, limits and source content.':
    'تعذرت معالجة المستند. راجع الصيغة والحجم والمحتوى.',
  'Select a text or PDF file up to 16 MB.': 'اختر ملفًا نصيًا أو PDF لا يتجاوز 16 ميجابايت.',
  Technician: 'فني',
  Supervisor: 'مشرف',
  Interrupted: 'انقطع التنفيذ',
};
