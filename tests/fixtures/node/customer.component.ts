// Angular Customer Component
export function Component(config: any) {
  return function (target: any) {};
}

@Component({
  selector: 'app-customer',
  templateUrl: './customer.component.html',
  styleUrls: ['./customer.component.scss']
})
export class CustomerComponent {
  title = 'Customer Portal';
}
