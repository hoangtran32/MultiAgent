/*---------------------------------------------------------------------------------------------
 * Copyright (c) Multi-Agent Hub contributors. Licensed under the MIT License.
 * Hub owns agent setup; a fresh editor never requires a default provider account.
 *--------------------------------------------------------------------------------------------*/
import { Emitter } from '../../../../base/common/event.js';
import { Disposable } from '../../../../base/common/lifecycle.js';
import { localize2 } from '../../../../nls.js';
import { Categories } from '../../../../platform/action/common/actionCommonCategories.js';
import { Action2, registerAction2 } from '../../../../platform/actions/common/actions.js';
import { ICommandService } from '../../../../platform/commands/common/commands.js';
import { InstantiationType, registerSingleton } from '../../../../platform/instantiation/common/extensions.js';
import { ServicesAccessor } from '../../../../platform/instantiation/common/instantiation.js';
import { IOnboardingService } from '../common/onboardingService.js';

class HubOnboardingService extends Disposable implements IOnboardingService {
    declare readonly _serviceBrand: undefined;
    private readonly dismissed = this._register(new Emitter<void>());
    readonly onDidDismiss = this.dismissed.event;
    constructor(@ICommandService private readonly commands: ICommandService) { super(); }
    show(): void {
        void this.commands.executeCommand('multiagent.open').then(
            () => this.dismissed.fire(),
            () => this.dismissed.fire()
        );
    }
}
registerSingleton(IOnboardingService, HubOnboardingService, InstantiationType.Delayed);
registerAction2(class extends Action2 {
    constructor() { super({ id:'workbench.action.welcomeOnboarding2026', title:localize2('hub.welcome', 'Welcome to Hub Code'), category:Categories.Help, f1:true }); }
    run(accessor: ServicesAccessor): void { accessor.get(IOnboardingService).show(); }
});
